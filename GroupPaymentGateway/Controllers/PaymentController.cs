
using AutoMapper;
using Microsoft.AspNetCore.Connections.Features;
using Newtonsoft.Json.Linq;
using NLog.Config;

namespace lk.Server.GroupPaymentGateway.Controllers
{
    [Route("v1/gateway/payments", Name = "Rotas de pagamento")]    
    public class PaymentController : BaseController
    {
        public PaymentController(ILoggerFactory LFactory, GatewayDBContext Context, IMapper Mapper, PaymentGatewayService PaymentGWay) : base(LFactory, Context, Mapper, PaymentGWay) { }


        /// <summary>
        /// Pagar fatura
        /// </summary>
        /// <remarks>Efetua o pagamento de uma fatura</remarks>
        [HttpPost("pay_invoice")]
        [ProducesResponseType(typeof(GwInvoice), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]        
        public async Task<ActionResult> PayInvoice([FromBody] JGwPayInvoice PayInvoice)
        {
            try
            {
                DbM_Invoice Invoice = await CheckInvoicePaymentAvailability(PayInvoice);

                JGwTransaction JTrasaction = await PrepareTransaction(PayInvoice, Invoice.ChargeNavigation.RecurrenceNavigation.UserNavigation, Invoice);

                GwTransaction NewTransaction = await PaymentGWay.CreateTransaction(JTrasaction, true);


                if(NewTransaction.Status == GwTransactionStatus.Paid || NewTransaction.Status == GwTransactionStatus.WaitingPayment)
                {
                    DbM_Invoice PaidInvoice = await Invoice.PayInvoice(DBContext, NewTransaction);

                    return MappedOk<GwInvoice>(PaidInvoice);
                }                    
                else                
                    return BadRequest(new GwModelCreationError(ErrorTypes.TransactionRefused, NewTransaction.RefuseMessage, NewTransaction.RefuseReason));                
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<PaymentController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<PaymentController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }

        private async Task<JGwTransaction> PrepareTransaction(JGwPayInvoice PInvoice, DbM_User User, DbM_Invoice DBInvoice)
        {
            JGwTransaction TPayment = new JGwTransaction();

            TPayment.Billing = new JGwBilling();
            TPayment.Billing.Name = "transacão GwGroupPayment";

            if(PInvoice.PaymentMethod == GwPaymentMethod.Boleto || PInvoice.PaymentMethod == GwPaymentMethod.CreditCard)
            {
                GwCustomer Customer = await PaymentGWay.CreateCustomer(PInvoice.PayerCustomer, true);
                TPayment.Customer = new JGwCustomer() { Id = Customer.Id };

                if (PInvoice.PaymentMethod == GwPaymentMethod.CreditCard)
                {
                    PInvoice.PayerCard.CustomerId = Customer.Id;

                    GwCard NewCard = await PaymentGWay.CreateCard(PInvoice.PayerCard, true);
                    TPayment.CardId = NewCard.Id.ToString();
                }
                else if(PInvoice.PaymentMethod == GwPaymentMethod.Boleto)
                {
                    TPayment.BoletoExpirationDate = DBInvoice.Expiration;
                    TPayment.BoletoInstructions = "Teste";
                }
            }


            TPayment.PaymentMethod = PInvoice.PaymentMethod;
            TPayment.TransactionType = GwTransactionType.Normal;
            TPayment.Amount = PInvoice.Amount;

            TPayment.Billing.Address = new JGwAddress();
            TPayment.Billing.Address.City = PInvoice.PayerCustomer.Address.City;
            TPayment.Billing.Address.Country = "br";
            TPayment.Billing.Address.Neighborhood = PInvoice.PayerCustomer.Address.Neighborhood;
            TPayment.Billing.Address.State = PInvoice.PayerCustomer.Address.State;
            TPayment.Billing.Address.Street = PInvoice.PayerCustomer.Address.Street;
            TPayment.Billing.Address.StreetNumber = PInvoice.PayerCustomer.Address.StreetNumber;
            TPayment.Billing.Address.Zipcode = PInvoice.PayerCustomer.Address.Zipcode;


            if (User.Name.Length > 13)
                TPayment.SoftDescriptor = User.Name.Substring(0, 13);
            else
                TPayment.SoftDescriptor = User.Name;


            JGwTransactionCalculation Calc = ApplyInvoiceRulesCalculation(DBInvoice);

            TPayment.SplitRules = Calc.SplitRules; 

            TPayment.PostbackUrl = DBContext.SystemConfigs.PostbackUrl;
            TPayment.PostbackUrl = "http://0dae-152-254-159-61.ngrok.io/v1/gateway/payments/payment_postback";
            TPayment.Installments = 1;


            return TPayment;
        }

        /// <summary>
        /// Forçar valor
        /// </summary>        
        /// <remarks>Altera o valor de uma fatura de forma manual. Dessa forma é possível ignorar as configurações da recorrència, como o valor, multas e descontos</remarks>
        [HttpPost("force_invoice_amount")]
        [ProducesResponseType(typeof(GwInvoice), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]       
        public async Task<ActionResult> ForceInvoiceAmount([FromBody] JGwForcedInvoice EditedInvoice)
        {
            DbM_User User = GetUser(EditedInvoice.ApiKey);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            try
            {
                DbM_Invoice Invoice = await CheckInvoicePaymentAvailability(EditedInvoice);
                Invoice.ForcedAmount = EditedInvoice.NewAmount;

                await DBContext.SaveChangesAsync();

                return MappedOk<GwInvoice>(Invoice);
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<PaymentController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<PaymentController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }

        /// <summary>
        /// Remover valor forçado
        /// </summary>        
        /// <remarks>Remove de uma fatura um valor adionado manualmente</remarks>
        [HttpPost("remove_forced_invoice_amount")]
        [ProducesResponseType(typeof(GwInvoice), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]        
        public async Task<ActionResult> RemoveForceInvoiceAmount([FromBody] JGwForcedInvoice EditedInvoice)
        {
            DbM_User User = GetUser(EditedInvoice.ApiKey);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            try
            {
                DbM_Invoice Invoice = await CheckInvoicePaymentAvailability(EditedInvoice);

                if (Invoice.ForcedAmount != null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.InvalidPaymentMethod, "fatura não está com valor alterado", ""));

                Invoice.ForcedAmount = null;

                await DBContext.SaveChangesAsync();

                return MappedOk<GwInvoice>(Invoice);
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<PaymentController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<PaymentController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }


        /// <summary>
        /// Pular cobrança
        /// </summary>        
        /// <remarks>Pula uma cobrança, ignoramento o pagamento da mesma e fazendo com que a recorrência prossiga para o próximo vencimento, caso exista</remarks>
        [HttpPost("skip_invoice")]
        [ProducesResponseType(typeof(GwInvoice), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]        
        public async Task<ActionResult> SkipInvoice([FromBody] JGwSkipInvoice SkipCharge)
        {
            DbM_User User = GetUser(SkipCharge.ApiKey);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            try
            {
                DbM_Invoice Invoice = await CheckInvoicePaymentAvailability(SkipCharge);

                return MappedOk<GwInvoice>(await Invoice.SkipInvoice(DBContext));
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<PaymentController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<PaymentController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }


        /// <summary>
        /// Fechar cobrança
        /// </summary>
        /// <remarks>Encerra uma cobrança que está atrelada ao usuário final. Dessa forma o usuário não será mais cobrado de valores</remarks>
        [HttpPost("close_charge")]
        [ProducesResponseType(typeof(GwCharge), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]        
        public async Task<ActionResult> CloseCharge([FromBody] JGwChargeClose Charge)
        {
            DbM_User User = GetUser(Charge.ApiKey);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            try
            {
                DbM_Charge OldCharge = DBContext.DPCharges.FirstOrDefault(x => x.Id == Charge.ChargeId);

                if(OldCharge == null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "cobrança não encontrada", nameof(JGwChargeClose.ChargeId).ToSnakeCase()));


                await OldCharge.CloseCharge(DBContext);

                return MappedOk<GwCharge>(OldCharge);
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<PaymentController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<PaymentController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }


        [HttpPost("payment_postback")]
        public async Task<ActionResult> PaymentPostback([FromBody] GwPostbackResponse Postback)
        {
            if(Postback.Object.TryToObject(out GwTransaction Transaction))
            {
                DbM_Invoice Invoice = DBContext.DPInvoices.FirstOrDefault(x => x.TransactionId == Transaction.Id);

                if(Invoice != null)
                {
                    try
                    {
                        if(Transaction.Status == GwTransactionStatus.Paid && Invoice.Status != GwChargeInvoiceStatus.Paid)                        
                            await Invoice.PayInvoice(DBContext, Transaction);                        
                        else if(Transaction.Status == GwTransactionStatus.Chargedback)
                        {

                        }

                        return Ok();
                    }
                    catch(Exception E)
                    {
                        ExceptionController.LogError<PaymentController>(E);
                        return BadRequest();
                    }
                }
                else
                {
                    ExceptionController.LogError<PaymentController>("Existe uma fatura sem transação mas com transação em andamento. TrId: " + Transaction.Id);
                }

                return Ok();
            }

            return BadRequest();
        }
    }
}