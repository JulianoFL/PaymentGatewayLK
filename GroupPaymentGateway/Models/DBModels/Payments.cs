using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Text;
using System.Linq;
using System.Threading.Tasks;

namespace lk.Server.GroupPaymentGateway.Models.DBModels
{
    public class DbM_Invoice
    {
        public int Id { get; set; }
        public int ChargeId { get; set; }
        public string TransactionId { get; set; }
        public int PaymentPointer { get; set; }
        public GwChargePaymentType Type { get; set; }
        public GwTransactionStatus TransactionStatus { get; private set; } = GwTransactionStatus.WaitingPayment;
        public GwPaymentMethod PaymentMethod { get; set; }
        public DateTime Expiration { get; set; }
        public DateTime DateCreated { get; set; }
        public int Amount { get; set; }
        public int? ForcedAmount { get; set; }
        public int? PayedAmount { get; set; }

        [NotMapped]
        public GwChargeInvoiceStatus Status
        {
            get
            {
                if(TransactionStatus != GwTransactionStatus.WaitingPayment)
                {
                    switch (TransactionStatus)
                    {
                        case GwTransactionStatus.Chargedback:
                            return GwChargeInvoiceStatus.Chargedback;                        
                        case GwTransactionStatus.Paid:
                            return GwChargeInvoiceStatus.Paid;                        
                        case GwTransactionStatus.Refunded:
                            return GwChargeInvoiceStatus.Refunded;                        
                        case GwTransactionStatus.Expired:
                            return GwChargeInvoiceStatus.Expired;

                        case GwTransactionStatus.Processing:
                        case GwTransactionStatus.PendingReview:
                        case GwTransactionStatus.PendingRefund:
                        case GwTransactionStatus.Refused:
                        case GwTransactionStatus.Authorized:                            
                        case GwTransactionStatus.WaitingPayment:
                            return GwChargeInvoiceStatus.WaitingPayment;
                        default:
                            return GwChargeInvoiceStatus.WaitingPayment;
                    }
                }
                else
                {
                    //Teoricamente, a expiração sempre será no futuro.
                    if (Expiration < DateTime.Now.ToUniversalTime())
                    {
                        if (!ChargeNavigation.RecurrenceNavigation.AllowPaymentAfterExpiration)
                            return GwChargeInvoiceStatus.Expired;
                        else
                        {
                            DbM_PaymentRule StopRule = ChargeNavigation.RecurrenceNavigation.GetPaymentRule(GwPaymentRuleType.StopPayment);

                            if (StopRule != null && Expiration.AddDays(-1 * (double)StopRule.Amount) < DateTime.Now.ToUniversalTime())
                                return GwChargeInvoiceStatus.Expired;
                            else
                                return GwChargeInvoiceStatus.WaitingExpiredPayment;
                        }
                    }

                    DbM_PaymentRule StartRule = ChargeNavigation.RecurrenceNavigation.GetPaymentRule(GwPaymentRuleType.StartPayment);

                    if (StartRule != null) 
                    {
                        if (DateTime.UtcNow.AddDays((int)StartRule.Amount).Date < Expiration.Date)
                            return GwChargeInvoiceStatus.Next;
                    }
                    
                    return GwChargeInvoiceStatus.WaitingPayment;
                }           
            }
        }


        [NotMapped]
        public int FinalAmount
        {
            get
            {
                if(PayedAmount != null)
                    return (int)PayedAmount;

                int TotalAmount = ForcedAmount == null ? ChargeNavigation.RecurrenceNavigation.Amount : (int)ForcedAmount;

                //Cobranca vencida
                if (Expiration.Date < DateTime.UtcNow.Date)
                {
                    //Cobranca vencida, com pagamento permitido
                    if (ChargeNavigation.RecurrenceNavigation.AllowPaymentAfterExpiration)
                    {
                        DbM_PaymentRule ExpirationFine = ChargeNavigation.RecurrenceNavigation.GetPaymentRule(GwPaymentRuleType.ExpirationFine);
                        if (ExpirationFine != null)
                        {
                            if (ExpirationFine.Percentage)
                                TotalAmount += (int)Math.Round(ExpirationFine.Amount * TotalAmount / 100, MidpointRounding.ToPositiveInfinity);
                            else
                                TotalAmount += (int)Math.Round(ExpirationFine.Amount, MidpointRounding.ToPositiveInfinity);
                        }

                        DbM_PaymentRule DailyFine = ChargeNavigation.RecurrenceNavigation.GetPaymentRule(GwPaymentRuleType.DailyFine);
                        if (DailyFine != null)
                        {
                            decimal ExpiredDays = (decimal)(DateTime.Now.ToUniversalTime() - Expiration).TotalDays;

                            if (DailyFine.Percentage)
                                TotalAmount += (int)Math.Round((DailyFine.Amount * TotalAmount / 100) * ExpiredDays, MidpointRounding.ToPositiveInfinity);
                            else
                                TotalAmount += (int)Math.Round(DailyFine.Amount * ExpiredDays, MidpointRounding.ToPositiveInfinity);
                        }

                        return TotalAmount;
                    }
                }
                else
                {
                    //Verifica se tem regras de decontos para pagamentos antes do vencimento
                    List<DbM_PaymentRule> BeforeExpiration = ChargeNavigation.RecurrenceNavigation.GetPaymentRules(GwPaymentRuleType.DiscountBeforeExpiration);
                    if (BeforeExpiration.IsNotNullOrEmpty())
                    {
                        decimal DiscountDays = (decimal)(Expiration - DateTime.Now.ToUniversalTime()).TotalDays;
                        DiscountDays++; //Um dia a mais para adicionar o dia do vencimento do disconto como válido para a regra de desconto

                        foreach (var Rule in BeforeExpiration)
                        {
                            if (DiscountDays > Rule.Days)
                            {
                                if (Rule.Percentage)
                                    TotalAmount -= (int)Math.Round(Rule.Amount * TotalAmount / 100, MidpointRounding.ToPositiveInfinity);
                                else
                                    TotalAmount -= (int)Math.Round(Rule.Amount, MidpointRounding.ToPositiveInfinity);
                            }
                        }
                    }
                }

                return TotalAmount;
            }
        }


        public virtual DbM_Charge ChargeNavigation { get; set; }
        public virtual DbM_InvoicePaymentInfo PaymentInfo { get; set; }



        public async Task<DbM_Invoice> PayInvoice(GatewayDBContext DBContext, [NotNull] GwTransaction PaymentTransaction)
        {
            return await UpdateInvoiceStatus(DBContext, null, PaymentTransaction);
        }

        public async Task<DbM_Invoice> SkipInvoice(GatewayDBContext DBContext)
        {
            return await UpdateInvoiceStatus(DBContext, GwChargeInvoiceStatus.Skipped, null);
        }

        private async Task<DbM_Invoice> UpdateInvoiceStatus(GatewayDBContext DBContext, GwChargeInvoiceStatus? NewStatus, GwTransaction PaymentTransaction)
        {
            if(PaymentTransaction == null)
            {
                if(NewStatus == GwChargeInvoiceStatus.Skipped || NewStatus == GwChargeInvoiceStatus.Closed)
                {
                    PaymentPointer = PaymentPointer;
                    DateCreated = DateTime.Now;
                    PaymentMethod = GwPaymentMethod.None;

                    if(NewStatus == GwChargeInvoiceStatus.Skipped)
                    {
                        Type = GwChargePaymentType.Skip;

                        ChargeNavigation.UpdateNextExpiration(DBContext.DPHolidays.ToList());
                    }
                    else if(NewStatus == GwChargeInvoiceStatus.Closed)
                    {
                        Type = GwChargePaymentType.Close;
                    }

                    await DBContext.SaveChangesAsync();

                    return this;
                }
            }
            else if(PaymentTransaction.Status == GwTransactionStatus.Paid || PaymentTransaction.Status == GwTransactionStatus.WaitingPayment)
            {
                PaymentPointer = PaymentPointer;
                PaymentMethod = PaymentTransaction.PaymentMethod;
                TransactionStatus = PaymentTransaction.Status;
                TransactionId = PaymentTransaction.Id;
                PayedAmount = PaymentTransaction.Amount;

                if(PaymentTransaction.Status == GwTransactionStatus.Paid)
                {
                    if(PaymentTransaction.PaymentMethod == GwPaymentMethod.CreditCard)
                    {
                        Type = GwChargePaymentType.Card;
                    }
                    else if(PaymentTransaction.PaymentMethod == GwPaymentMethod.Boleto)
                    {
                        Type = GwChargePaymentType.Boleto;
                    }
                    else if(PaymentTransaction.PaymentMethod == GwPaymentMethod.PIX)
                    {
                        Type = GwChargePaymentType.PIX;
                    }

                    ChargeNavigation.UpdateNextExpiration(DBContext.DPHolidays.ToList());
                }
                else if(PaymentTransaction.Status == GwTransactionStatus.WaitingPayment)
                {
                    if(PaymentMethod == GwPaymentMethod.Boleto)
                    {
                        Type = GwChargePaymentType.Boleto;

                        DbM_InvoicePaymentInfo BInfo = new DbM_InvoicePaymentInfo();

                        BInfo.DateCreated = DateTime.UtcNow;
                        BInfo.Url = PaymentTransaction.BoletoUrl;
                        BInfo.Code = PaymentTransaction.BoletoBarcode;
                        BInfo.Expiration = ((DateTime)PaymentTransaction.BoletoExpirationDate).ToUniversalTime();

                        PaymentInfo = BInfo;
                    }
                    else if(PaymentMethod == GwPaymentMethod.PIX)
                    {
                        Type = GwChargePaymentType.PIX;

                        DbM_InvoicePaymentInfo PInfo = new DbM_InvoicePaymentInfo();

                        PInfo.DateCreated = DateTime.UtcNow;
                        PInfo.Url = PaymentTransaction.PixInfos.Link;
                        PInfo.Code = PaymentTransaction.PixInfos.QrCode;
                        PInfo.Expiration = PaymentTransaction.PixInfos.Expiration.ToUniversalTime();

                        PaymentInfo = PInfo;
                    }
                }

                await DBContext.SaveChangesAsync();

                return this;
            }


            ExceptionController.LogError<DbM_Charge>("Ocorreu uma falha ao atualizar um pagamento de uma cobrança");

            throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.PaymentError, "ocorreu uma falha ao atualizar a cobrança. Por favor tente novamente", "-"));
        }
    }


    public class DbM_InvoicePaymentInfo
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        public string Url { get; set; }
        public string Code { get; set; }
        public DateTime Expiration { get; set; }
        public DateTime DateCreated { get; set; }


        public virtual DbM_Invoice InvoiceNavigation { get; set; }
    }
}
