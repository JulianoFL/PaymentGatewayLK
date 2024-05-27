
using AutoMapper;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using lk.Server.GroupPaymentGateway.Models.DBModels;
using lk.Shared.Models.Gateway.GroupPayment;

namespace lk.Server.GroupPaymentGateway.Controllers
{
    [ApiController]
    [Produces("application/json")]
    [Consumes("application/json")]
    public abstract class BaseController : ControllerBase
    {
        public GatewayDBContext DBContext { get; private set; }

        public PaymentGatewayService PaymentGWay { get; set; }

        public ILoggerFactory LFactory { get; set; }

        public IMapper AtMapper { get; set; }


        protected BaseController(ILoggerFactory LFactory, GatewayDBContext Context, IMapper Mapper, PaymentGatewayService PaymentGWay)
        {
            DBContext = Context;

            AtMapper = Mapper;

            this.LFactory = LFactory;

            this.PaymentGWay = PaymentGWay;
        }

        //------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Retorna o ObjLogin do usuário que fez a chamada
        /// </summary>
        internal DbM_User GetUser(string ApiKey)
        {
            DbM_User User = DBContext.DPUsers.FirstOrDefault(x => x.ApiKey == ApiKey);


            return User;
        }

        //------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        private List<JGwSplitRule> TransformRules(int Amount, List<GwRecurrenceSplitRule> SplitRules)
        {
            //- Possível BUG = Quando adicionar recorrencia, ou mudar seu valor, pode ocorrer do pagador da taxa não ter valor a receber


            List<JGwSplitRule> JRules = new List<JGwSplitRule>();
                       
            foreach (var Rule in SplitRules)
            {
                JGwSplitRule JRule = new JGwSplitRule();

                //if (Rule.Percentage == true)
                //{
                //    JRule.Amount = (int)Math.Round((double)Rule.Amount * Amount, MidpointRounding.ToPositiveInfinity);
                //    JRule.Liable = Rule.Liable;
                //    JRule.ChargeProcessingFee = Rule.ChargeProcessingFee;
                //    JRule.RecipientId = Rule.RecipientId;
                //}
                //else
                //{
                    JRule.Amount = Rule.Amount;
                    JRule.Liable = Rule.Liable;
                    JRule.ChargeProcessingFee = Rule.ChargeProcessingFee;
                    JRule.RecipientId = Rule.RecipientId;
                //}

                JRules.Add(JRule);
            }


            //Retira do chargeback diferenças de arredendamento
            int SplitsSumTaxes = JRules.Sum(x => x.Amount);
            if (Amount != SplitsSumTaxes)
            {
                int Diff = Amount - SplitsSumTaxes;

                JRules.FirstOrDefault(x => x.Liable == true).Amount -= Diff;
            }


            return JRules;
        }

        //------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        internal async Task<DbM_Invoice> CheckInvoicePaymentAvailability<T>(T Invoice) where T : JInvoice
        {
            DbM_Invoice DBInvoice = await DBContext.DPInvoices.Include(x => x.ChargeNavigation).FirstOrDefaultAsync(x => x.Id == Invoice.InvoiceId);

            if (DBInvoice == null)
                throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.NotFound, "fatura não encontrada", nameof(JGwPayInvoice.InvoiceId).ToSnakeCase()));

            if (typeof(T) == typeof(JGwPayInvoice))
            {
                JGwPayInvoice JGw = Invoice as JGwPayInvoice;

                if (!DBInvoice.ChargeNavigation.RecurrenceNavigation.PaymentMethods.Contains((GwPaymentMethod)JGw.PaymentMethod)) //Quando o método de pagamento não foi configurado na recorrencia
                    throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.InvalidPaymentMethod, "método de pagmento inválido", nameof(JGwPayInvoice.PaymentMethod).ToSnakeCase()));

                if (DBInvoice.Status != GwChargeInvoiceStatus.WaitingPayment && DBInvoice.Status != GwChargeInvoiceStatus.WaitingExpiredPayment) //Qualquer outro estado além de esperando pagamento não aceita mais o pagamento                
                    throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.InvalidStatus, "fatura não pode ser paga", DBInvoice.Status.ToSnakeCase()));

                //if (DBInvoice.ChargeNavigation.Status != GwChargeStatus.WaitingPayment && DBInvoice.Status != GwChargeStatus.WaitingExpiredPayment) //Qualquer outro estado além de esperando pagamento não aceita mais o pagamento                
                //    throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.InvalidStatus, "fatura não pode ser paga", DBInvoice.Status.ToSnakeCase()));

                if (JGw.Amount != DBInvoice.FinalAmount)
                    throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.InvalidAmount, "valor da fatura diverge do valor enviado", nameof(JGwPayInvoice.Amount).ToSnakeCase()));

                if (DBInvoice.Status == GwChargeInvoiceStatus.Paid)
                    throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.InvalidStatus, "fatura já paga", DBInvoice.Status.ToSnakeCase()));

                if (DBInvoice.Status == GwChargeInvoiceStatus.Next)
                    throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.StartDateRule, "a regra de início de pagamento não foi antendida", "payment_rule[start_payment]"));


                if (JGw.PaymentMethod == GwPaymentMethod.Boleto)
                {
                    if(DBInvoice.PaymentInfo != null)
                    {
                        if (DBInvoice.PaymentInfo.Expiration.Date > DateTime.UtcNow.Date)
                            throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.InvalidAmount, "já existe um pagamento em andamento para essa fatura (" + DBInvoice.PaymentInfo.Id + ")", nameof(JGwPayInvoice.Amount).ToSnakeCase()));
                    }
                }
            }
            else if (typeof(T) == typeof(JGwSkipInvoice))
            {
                JGwSkipInvoice JGw = Invoice as JGwSkipInvoice;

                if ((DBInvoice.Status != GwChargeInvoiceStatus.WaitingPayment || DBInvoice.Status != GwChargeInvoiceStatus.WaitingExpiredPayment) && DBInvoice.Status != GwChargeInvoiceStatus.Expired) //Qualquer outro estado além de esperando ou expirada
                    throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.InvalidStatus, "fatura não pode ignorada, ainda espera pagamento", DBInvoice.Status.ToSnakeCase()));

                if (JGw.Amount != DBInvoice.FinalAmount)
                    throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.InvalidAmount, "valor da fatura diverge do valor enviado", nameof(JGwPayInvoice.Amount).ToSnakeCase()));
            }
            else if (typeof(T) == typeof(JGwForcedInvoice))
            {
                JGwForcedInvoice JGw = Invoice as JGwForcedInvoice;

                if (DBInvoice.Status != GwChargeInvoiceStatus.WaitingPayment && DBInvoice.Status != GwChargeInvoiceStatus.Expired)
                    throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.InvalidStatus, "fatura não pode ter o valor alterado", DBInvoice.Status.ToSnakeCase()));
            }
            else if(typeof(T) == typeof(JGwChargeClose))
            {
                if (DBInvoice.Status == GwChargeInvoiceStatus.Closed)
                    throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.Closed, "fatura já fechada", "-"));
            }


            return DBInvoice;
        }

        //------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        internal JGwTransactionCalculation ApplyInvoiceRulesCalculation(DbM_Invoice Invoice)
        {
            //Fatura vencida
            if(Invoice.Expiration.Date < DateTime.UtcNow.Date)            
                return CalculateInvoiceFineRules(Invoice.ChargeNavigation.RecurrenceNavigation);            
            else            
                return CalculateInvoiceDiscountRules(Invoice.ChargeNavigation.RecurrenceNavigation, Invoice.Expiration);            
        }


        internal JGwTransactionCalculation CalculateInvoiceWithoutRules(DbM_Recurrence Recurrence)
        {
            JGwTransactionCalculation TPayment = new JGwTransactionCalculation();

            TPayment.Amount = Recurrence.Amount;


            GwRecurrenceSplitRule NegativeRule = Recurrence.SplitRules.FirstOrDefault(x => x.Amount < 1);
            if (NegativeRule != null)
                throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.NegativeSplitAmount, "um dos recebedores terá um valor negativo calculado os descontos", NegativeRule.RecipientId + ";" + NegativeRule.Amount.ToString()));


            TPayment.SplitRules = TransformRules((int)TPayment.Amount, AtMapper.Map<List<GwRecurrenceSplitRule>>(Recurrence.SplitRules));

            return TPayment;

        }

        /// <summary>
        /// Calcula o valor dos splits e transação para as regras de desconto, ignorando datas
        /// </summary>
        internal JGwTransactionCalculation CalculateInvoiceDiscountRules(DbM_Recurrence Recurrence, DateTime? InvoiceExpiration = null)
        {
            JGwTransactionCalculation DiscountTPayment = new JGwTransactionCalculation();
            List<GwRecurrenceSplitRule> DiscountSplits = Recurrence.SplitRules.Copy();

            //Verifica se aplicando os descontos, algum recebedor não fica negativo
            List<DbM_PaymentRule> DiscountRules = Recurrence.PaymentRules.Where(x => x.Type == GwPaymentRuleType.DiscountBeforeExpiration).ToList();

            if (DiscountRules.IsNotNullOrEmpty())
            {

                DiscountTPayment.Amount = Recurrence.Amount;

                List<GwRecurrenceSplitRule> ApplySplitRules = DiscountSplits.Where(x => x.ApplyPaymentRules == true).ToList();

                if (!ApplySplitRules.IsNotNullOrEmpty()) //Caso não tenha ninguém para aplicar as regras de pagamento, escolhe o responsãvel pelo chargeback
                    ApplySplitRules = new List<GwRecurrenceSplitRule>() { DiscountSplits.FirstOrDefault(x => x.Liable == true) };


                foreach (var Split in ApplySplitRules)
                {
                    foreach (var Rule in DiscountRules)
                    {
                        if (InvoiceExpiration == null || (InvoiceExpiration - DateTime.UtcNow.Date).Value.Days >= Rule.Days)
                        {
                            if (Rule.Percentage == true)
                            {
                                int DiscountAmount = (int)(Rule.Amount * Recurrence.Amount) / 100 / ApplySplitRules.Count;

                                DiscountTPayment.Amount -= DiscountAmount;
                                Split.Amount -= DiscountAmount;
                            }
                            else
                            {
                                int DiscountAmount = (int)Rule.Amount / ApplySplitRules.Count;

                                DiscountTPayment.Amount -= DiscountAmount;
                                Split.Amount -= DiscountAmount;
                            }
                        }
                    }
                }

                DiscountTPayment.SplitRules = TransformRules((int)DiscountTPayment.Amount, AtMapper.Map<List<GwRecurrenceSplitRule>>(DiscountSplits));

            }


            GwRecurrenceSplitRule NegativeRule = DiscountSplits.FirstOrDefault(x => x.Amount < 1);
            if (NegativeRule != null)
                throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.NegativeSplitAmount, "um dos recebedores terá um valor negativo calculado os descontos", NegativeRule.RecipientId + ";" + NegativeRule.Amount.ToString()));


            DiscountTPayment.SplitRules = TransformRules((int)DiscountTPayment.Amount, AtMapper.Map<List<GwRecurrenceSplitRule>>(DiscountSplits));

            return DiscountTPayment;
        }

        /// <summary>
        /// Calcula o valor dos splits e transação para as regras de multas, ignorando datas
        /// </summary>
        internal JGwTransactionCalculation CalculateInvoiceFineRules(DbM_Recurrence Recurrence)
        {
            JGwTransactionCalculation FineTPayment = new JGwTransactionCalculation();
            List<GwRecurrenceSplitRule> FineSplits = Recurrence.SplitRules.Copy();

            FineTPayment.Amount = Recurrence.Amount;

            //Verifica se aplicando as multas, algum recebedor não fica negativo
            List<DbM_PaymentRule> FineRules = Recurrence.PaymentRules.Where(x => x.Type == GwPaymentRuleType.DailyFine || x.Type == GwPaymentRuleType.ExpirationFine).ToList();

            if (FineRules.IsNotNullOrEmpty())
            {
                FineTPayment.Amount = Recurrence.Amount;

                List<GwRecurrenceSplitRule> ApplySplitRules = FineSplits.Where(x => x.ApplyPaymentRules == true).ToList();

                if (!ApplySplitRules.IsNotNullOrEmpty()) //Caso não tenha ninguém para aplicar as regras de pagamento, escolhe o responsãvel pelo chargeback
                    ApplySplitRules = new List<GwRecurrenceSplitRule>() { FineSplits.FirstOrDefault(x => x.Liable == true) };


                foreach (var Split in ApplySplitRules)
                {
                    foreach (var Rule in FineRules)
                    {
                        if (Rule.Type == GwPaymentRuleType.ExpirationFine)
                        {
                            if (Rule.Percentage == true)
                            {
                                int FineAmount = (int)(Rule.Amount * Recurrence.Amount) / 100 / ApplySplitRules.Count;

                                FineTPayment.Amount += FineAmount;
                                Split.Amount += FineAmount;
                            }
                            else
                            {
                                int FineAmount = (int)Rule.Amount / FineRules.Count;

                                FineTPayment.Amount += FineAmount;
                                Split.Amount += FineAmount;
                            }
                        }


                        if (Rule.Type == GwPaymentRuleType.DailyFine)
                        {
                            int StopPaymentDays = (int)Recurrence.PaymentRules.FirstOrDefault(x => x.Type == GwPaymentRuleType.StopPayment).Amount;

                            if (Rule.Percentage == true)
                            {
                                int FineAmount = (int)(Rule.Amount * StopPaymentDays * Recurrence.Amount) / 100 / ApplySplitRules.Count;

                                FineTPayment.Amount += FineAmount;
                                Split.Amount += FineAmount;
                            }
                            else
                            {
                                int FineAmount = (int)(Rule.Amount * StopPaymentDays) / ApplySplitRules.Count;

                                FineTPayment.Amount += FineAmount;
                                Split.Amount += FineAmount;
                            }
                        }
                    }
                }
            }


            GwRecurrenceSplitRule NegativeRule = FineSplits.FirstOrDefault(x => x.Amount < 1);
            if (NegativeRule != null)
                throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.NegativeSplitAmount, "um dos recebedores terá um valor negativo calculado os descontos", NegativeRule.RecipientId + ";" + NegativeRule.Amount.ToString()));


            FineTPayment.SplitRules = TransformRules((int)FineTPayment.Amount, AtMapper.Map<List<GwRecurrenceSplitRule>>(FineSplits));

            return FineTPayment;
        }

        //------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

        internal GwModelCreationError GetValidationErros()
        {
            Dictionary<string, ModelError> ErrorList = ModelState.Where(x => x.Value.Errors.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.FirstOrDefault());


            GwModelCreationError Errors = new GwModelCreationError();

            foreach (var item in ErrorList)            
                Errors.Errors.Add(new GwModelError(ErrorTypes.InvalidParameter, item.Value.ErrorMessage, item.Key));
            

            return Errors;
        }

        internal OkObjectResult MappedOk<T>(object Return)
        {
            return Ok(AtMapper.Map<T>(Return));
        }
    }
}
