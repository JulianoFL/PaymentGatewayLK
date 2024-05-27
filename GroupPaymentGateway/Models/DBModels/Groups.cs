
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using lk.Server.GroupPaymentGateway.Controllers;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace lk.Server.GroupPaymentGateway.Models.DBModels
{
    public partial class DbM_Group 
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int MaxItems { get; set; }
        public DateTime DateCreated { get; set; }


        public virtual DbM_User UserNavigation { get; set; }

        public virtual List<DbM_Recurrence> Recurrences { get; set; }

        public bool ContainsRecurrence(string RecurrenceName) => Recurrences.Any(x => x.Name == RecurrenceName);
    }

    public class DbM_Recurrence 
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int? GroupId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public GwRecurrenceStatus Status { get; set; }
        public string EndUserName { get; set; }
        public string EndUserComment { get; set; }
        public int Amount { get; set; }
        public int Interval { get; set; }
        public List<GwPaymentMethod> PaymentMethods { get; set; }
        public List<GwRecurrenceSplitRule> SplitRules { get; set; }
        public GwRecurrenceIntervalType IntervalType { get; set; }
        public int StartAfterDays { get; set; }
        public bool AllowPaymentAfterExpiration { get; set; }
        public DateTime DateUpdated { get; set; }
        public DateTime ActivationDate { get; set; }
        public DateTime DateCreated { get; set; }


        public virtual List<DbM_PaymentRule> PaymentRules { get; set; }

        public virtual List<DbM_Charge> Charges { get; set; }

        public virtual List<DbM_RecurrenceNotificationSettings> NotificationSettings { get; set; }


        public DbM_Charge GetChargeByEUserId(int EndUserId) => Charges.FirstOrDefault(x => x.EndUserId == EndUserId);

        public virtual DbM_User UserNavigation { get; set; }

        public virtual DbM_Group GroupNavigation { get; set; }

        public DbM_PaymentRule GetPaymentRule(GwPaymentRuleType RuleType)
        {
            if (PaymentRules.IsNotNullOrEmpty())
                return PaymentRules.FirstOrDefault(x => x.Type == RuleType);
            else
                return null;            
        }
        public List<DbM_PaymentRule> GetPaymentRules(GwPaymentRuleType RuleType)
        {
            if (PaymentRules.IsNotNullOrEmpty())
                return PaymentRules.Where(x => x.Type == RuleType).OrderBy(x => x.Days).ToList();
            else
                return null;
        }
    }

    public class DbM_Charge
    {
        public int Id { get; set; }
        public int RecurrenceId { get; set; }
        public int EndUserId { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime NextExpiration { get; set; }
        public int PaymentPointer { get; set; } = 1;
        public bool IgnorePaymentRules { get; set; }


        [NotMapped]
        public GwChargeInvoiceStatus Status
        {
            get
            {
                if (PaymentPointer == int.MaxValue)
                    return GwChargeInvoiceStatus.Closed;
                else if (SortedInvoices.Any(x => x.Status == GwChargeInvoiceStatus.Chargedback))
                    return GwChargeInvoiceStatus.Chargedback;
                else if (SortedInvoices.Any(x => x.Status == GwChargeInvoiceStatus.Refunded))
                    return GwChargeInvoiceStatus.Refunded;
                else if(SortedInvoices.Any(x => x.Status == GwChargeInvoiceStatus.Expired))
                    return GwChargeInvoiceStatus.Expired;
                else if(SortedInvoices.Any(x => x.Status == GwChargeInvoiceStatus.WaitingPayment))
                    return GwChargeInvoiceStatus.WaitingPayment;
                else if (SortedInvoices.Any(x => x.Status == GwChargeInvoiceStatus.WaitingExpiredPayment))
                    return GwChargeInvoiceStatus.WaitingExpiredPayment;
                else
                    return GwChargeInvoiceStatus.Paid;
            }
        }

        [NotMapped]
        public int ChargeTotalOpenAmount
        {
            get
            {
                if (SortedInvoices.IsNotNullOrEmpty())
                {
                    List<DbM_Invoice> PaidInvoices = SortedInvoices.Where(x => x.Status != GwChargeInvoiceStatus.Paid).ToList();

                    if (PaidInvoices.IsNotNullOrEmpty())
                        return SortedInvoices.Where(x => x.Status != GwChargeInvoiceStatus.Paid).Sum(x => x.FinalAmount);
                }

                return 0;
            }
        }

        [NotMapped]
        public DbM_Invoice CurrentInvoice
        {
            get
            {
                if (SortedInvoices.IsNotNullOrEmpty())
                    return SortedInvoices.OrderByDescending(x => x.DateCreated).FirstOrDefault(x => x.PaymentPointer == PaymentPointer);
                else
                    return null;
            }
        }

        
        public DbM_Invoice LastInvoice
        {
            get
            {
                if (SortedInvoices.IsNotNullOrEmpty())
                    return SortedInvoices.OrderByDescending(x => x.DateCreated).FirstOrDefault(x => x.PaymentPointer == PaymentPointer - 1);
                else
                    return null;
            }
        }


        [NotMapped]
        public List<DbM_Invoice> SortedInvoices
        {
            get
            {
                if (Invoices.IsNotNullOrEmpty())
                    return Invoices.OrderByDescending(x => x.DateCreated).ToList();
                else
                    return Invoices;
            }

            private set { Invoices = value; }
        }
        public virtual List<DbM_Invoice> Invoices { get; set; } = new List<DbM_Invoice>();

        public virtual DbM_Recurrence RecurrenceNavigation { get; set; }
        public virtual DbM_EndUser EndUserNavigation { get; set; }


        //Atualiza a data de vencimento da cobrança. Deve ser utilizado depois de adicionar o pagamento atual aos pagamentos da cobrança
        //Apos atualizado a data, abre um novo pagamento com estado de aberto
        public void UpdateNextExpiration(List<DbM_Holiday> Holidays)
        {            
            if(CurrentInvoice == null || CurrentInvoice.Status == GwChargeInvoiceStatus.Paid)
            {
                DateTime Start = DateCreated.AddDays(RecurrenceNavigation.StartAfterDays);


                if(RecurrenceNavigation.IntervalType == GwRecurrenceIntervalType.Yearly)
                {
                    NextExpiration = Start.AddYears(RecurrenceNavigation.Interval * PaymentPointer).ToUniversalTime();
                }
                else if(RecurrenceNavigation.IntervalType == GwRecurrenceIntervalType.Weekly)
                {
                    NextExpiration = Start.AddDays(RecurrenceNavigation.Interval * 7 * PaymentPointer).ToUniversalTime();
                }
                else
                {
                    NextExpiration = Start.AddMonths(RecurrenceNavigation.Interval * PaymentPointer).ToUniversalTime();
                }


                NextExpiration = new DateTime(NextExpiration.Year, NextExpiration.Month, NextExpiration.Day, 0, 0, 0, DateTimeKind.Utc);


                while(Holidays.Any(x => x.Date == NextExpiration) || NextExpiration.DayOfWeek == DayOfWeek.Saturday || NextExpiration.DayOfWeek == DayOfWeek.Sunday)
                    NextExpiration = NextExpiration.AddDays(1);


                DbM_Invoice Payment = new DbM_Invoice();

                Payment.PaymentPointer = PaymentPointer;
                Payment.DateCreated = DateTime.Now;
                Payment.PaymentMethod = GwPaymentMethod.None;
                Payment.Type = GwChargePaymentType.Open;
                Payment.Expiration = NextExpiration;
                Payment.Amount = RecurrenceNavigation.Amount;

                Invoices.Add(Payment);

                PaymentPointer++;
            }            
        }

        public async Task<DbM_Charge> CloseCharge(GatewayDBContext DBContext)
        {
            DbM_Charge Payment = this;

            Payment.PaymentPointer = int.MaxValue;            

            await DBContext.SaveChangesAsync();

            return Payment;

        }

        //public async Task<DbM_Invoice> PayCharge(GatewayDBContext DBContext, [NotNull] GwTransaction PaymentTransaction)
        //{
        //    return await UpdateChargeStatus(DBContext, null, PaymentTransaction);
        //}

        //public async Task<DbM_Invoice> SkipCharge(GatewayDBContext DBContext)
        //{
        //    return await UpdateChargeStatus(DBContext, GwChargeInvoiceStatus.Skipped, null);
        //}

        //private async Task<DbM_Invoice> UpdateChargeStatus(GatewayDBContext DBContext, GwChargeInvoiceStatus? NewStatus, GwTransaction PaymentTransaction)
        //{
        //    if (PaymentTransaction == null)
        //    {
        //        if (NewStatus == GwChargeInvoiceStatus.Skipped || NewStatus == GwChargeInvoiceStatus.Closed)
        //        {
        //            DbM_Invoice Payment = CurrentPayment;

        //            Payment.PaymentPointer = PaymentPointer;
        //            Payment.DateCreated = DateTime.Now;
        //            Payment.PaymentMethod = GwPaymentMethod.None;
        //            Payment.Status = GwTransactionStatus.Paid;

        //            if (NewStatus == GwChargeInvoiceStatus.Skipped)
        //            {
        //                Payment.Type = GwChargePaymentType.Skip;

        //                UpdateNextExpiration(RecurrenceNavigation, DBContext.DPHolidays.ToList());
        //            }
        //            else if (NewStatus == GwChargeInvoiceStatus.Closed)
        //            {
        //                Payment.Type = GwChargePaymentType.Close;
        //            }

        //            await DBContext.SaveChangesAsync();

        //            return Payment;
        //        }
        //    }
        //    else if (PaymentTransaction.Status == GwTransactionStatus.Paid || PaymentTransaction.Status == GwTransactionStatus.WaitingPayment)
        //    {
        //        DbM_Invoice Payment = CurrentPayment;

        //        Payment.PaymentPointer = PaymentPointer;
        //        Payment.PaymentMethod = PaymentTransaction.PaymentMethod;
        //        Payment.Status = PaymentTransaction.Status;
        //        Payment.TransactionId = PaymentTransaction.Id;

        //        switch (PaymentTransaction.PaymentMethod)
        //        {
        //            case GwPaymentMethod.CreditCard:
        //                Payment.Type = GwChargePaymentType.Card;
        //                break;
        //            case GwPaymentMethod.Boleto:
        //                Payment.Type = GwChargePaymentType.Boleto;


        //                DbM_InvoicePaymentInfo BInfo = new DbM_InvoicePaymentInfo();

        //                BInfo.DateCreated = DateTime.Now.ToUniversalTime();
        //                BInfo.Url = PaymentTransaction.BoletoUrl;
        //                BInfo.Code = PaymentTransaction.BoletoBarcode;
        //                BInfo.Expiration = (DateTime)PaymentTransaction.BoletoExpirationDate;

        //                Payment.InvoicePayment = BInfo;

        //                break;
        //            case GwPaymentMethod.PIX:
        //                Payment.Type = GwChargePaymentType.PIX;

        //                DbM_InvoicePaymentInfo PInfo = new DbM_InvoicePaymentInfo();

        //                PInfo.DateCreated = DateTime.Now.ToUniversalTime();
        //                PInfo.Url = PaymentTransaction.PixInfos.Link;
        //                PInfo.Code = PaymentTransaction.PixInfos.QrCode;
        //                PInfo.Expiration = PaymentTransaction.PixInfos.Expiration;

        //                Payment.InvoicePayment = PInfo;

        //                break;
        //        }

        //        UpdateNextExpiration(RecurrenceNavigation, DBContext.DPHolidays.ToList());


        //        await DBContext.SaveChangesAsync();

        //        return Payment;
        //    }


        //    ExceptionController.LogError<DbM_Charge>("Ocorreu uma falha ao atualizar um pagamento de uma cobrança");

        //    throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.PaymentError, "ocorreu uma falha ao atualizar a cobrança. Por favor tente novamente", "-"));        }

        public GwEndUserCharge CreateGwEndUserCharge(IMapper Mapper)
        {
            GwEndUserCharge Model = new GwEndUserCharge();
            Model.Id = Id;
            Model.DateCreated = DateCreated;
            Model.Expiration = NextExpiration;
            Model.RecurrenceId = RecurrenceNavigation.Id;
            Model.RecurrenceDescription = RecurrenceNavigation.Description;
            Model.RecurrenceName = RecurrenceNavigation.Name;
            Model.Status = Status;
            Model.PaymentMethods = RecurrenceNavigation.PaymentMethods;
            Model.ChargeInvoices = Mapper.Map<List<GwInvoice>>(SortedInvoices);
            Model.ChargeTotalOpenAmount = ChargeTotalOpenAmount;


            return Model;
        }
    }

    public class DbM_PaymentRule
    {
        public int Id { get; set; }
        public int RecurrenceId { get; set; }
        public GwPaymentRuleType Type { get; set; }
        public int Days { get; set; }
        public decimal Amount { get; set; }
        public bool Percentage { get; set; }


        public virtual DbM_Recurrence RecurrenceNavigation { get; set; }
    }
}
