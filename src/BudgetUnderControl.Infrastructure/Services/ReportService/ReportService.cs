﻿using BudgetUnderControl.Common.Contracts;
using BudgetUnderControl.CommonInfrastructure;
using BudgetUnderControl.Domain;
using BudgetUnderControl.Domain.Repositiories;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms.Internals;
using static MoreLinq.Extensions.MinByExtension;

namespace BudgetUnderControl.Infrastructure.Services
{
    public class ReportService : IReportService
    {
        private readonly ITransactionService transactionService;
        private readonly ICurrencyRepository currencyRepository;
        private readonly IAccountService accountService;
        private readonly ICurrencyService currencyService;

        public ReportService(ITransactionService transactionService 
            ,ICurrencyRepository currencyRepository
            , IAccountService accountService
            , ICurrencyService currencyService)
        {
            this.transactionService = transactionService;
            this.currencyRepository = currencyRepository;
            this.accountService = accountService;
            this.currencyService = currencyService;
        }

        public async Task<ICollection<MovingSumItemDTO>> MovingSum(TransactionsFilter filter = null)
        {
            var transactions = await this.transactionService.GetTransactionsAsync(filter);
           
            var movingSumCollection = new List<MovingSumItemDTO>();
            decimal sum = 0;
            var recalculateCurrencies = false;
            var selectedCurrencyCode = "PLN";
            List<ExchangeRate> exchangeRates = null;

            if (recalculateCurrencies)
            {
                 exchangeRates = (await this.currencyRepository.GetExchangeRatesAsync()).ToList();
            }

            ((transactions.OrderBy(x => x.Date))).ForEach(x =>
            {
                var diff = 0m;
                if (recalculateCurrencies)
                {
                   diff = (x.CurrencyCode == selectedCurrencyCode ? x.Value : this.GetValueInCurrency(exchangeRates, x.CurrencyCode, selectedCurrencyCode, x.Value, x.JustDate));
                }
                else {
                    diff = (x.CurrencyCode == selectedCurrencyCode ? x.Value : 0m);
                }
                sum += diff;
                var movingSum = new MovingSumItemDTO
                {
                    Date = x.JustDate,
                    Currency = x.CurrencyCode,
                    Value = sum,
                    Diff = diff,
                };
                movingSumCollection.Add(movingSum);
            });

            return movingSumCollection;
        }

        public async Task<DashboardDTO> GetDashboard()
        {
            var dashboard = new DashboardDTO();
            var allTransactions = await this.transactionService.GetTransactionsAsync();

            Dictionary<string, decimal> dict = new Dictionary<string, decimal>();
            var accounts = await accountService.GetAccountsWithBalanceAsync();

            foreach (var account in accounts)
            {
                if (!account.ParentAccountId.HasValue)
                {
                    if (!dict.ContainsKey(account.Currency))
                    {
                        dict.Add(account.Currency, account.Balance);
                    }
                    else
                    {
                        dict[account.Currency] += account.Balance;
                    }
                }

            }

            dashboard.ActualStatus = dict;

            string userMainCurrency = "PLN";
            decimal sum = 0;
            dict.ForEach(async x =>
            {
                sum += (await this.CalculateValueAsync(x.Value, x.Key, userMainCurrency));
            });

            dashboard.Total = sum;

            return dashboard;
        }

        private async Task<decimal> CalculateValueAsync(decimal amount, string fromCurrencyCode, string toCurrencyCode)
        {
            var value = await this.currencyService.TransformAmountAsync(amount, fromCurrencyCode, toCurrencyCode);
            return value;
        }

        private decimal GetValueInCurrency(IList<ExchangeRate> rates, string currentCurrency, string targetCurrency, decimal value, DateTime date)
        {
           var exchangeRate = rates.Where(x => x.ToCurrency.Code == currentCurrency || x.FromCurrency.Code == currentCurrency)
                                   .Where(x => x.ToCurrency.Code == targetCurrency || x.FromCurrency.Code == targetCurrency)
                                   .MinBy(x => Math.Abs((x.Date - date).Ticks))
                                    .FirstOrDefault();

            decimal result = 0;
            if (exchangeRate == null)
            {
                result = 0;
            }
            else if (exchangeRate.FromCurrency.Code == currentCurrency)
            {
                result = value * (decimal)exchangeRate.Rate;
            }
            else if (exchangeRate.ToCurrency.Code == currentCurrency)
            {
                result = value / ((decimal)exchangeRate.Rate != 0 ? (decimal)exchangeRate.Rate : 1);
            }

            return result;
        }

    }
}
