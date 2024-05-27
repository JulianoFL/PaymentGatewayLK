
using AutoMapper;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using lk.Shared.Models.Gateway.BaseClasses.Parameters;
using System.Text.RegularExpressions;

namespace lk.Server.GroupPaymentGateway.Controllers
{
    //http://localhost/swagger/v1/swagger.json
    [Route("v1/gateway/groups", Name = "Rotas de grupo")]
    public class GroupController : BaseController
    {
        public GroupController(ILoggerFactory LFactory, GatewayDBContext Context, IMapper Mapper, PaymentGatewayService PaymentGWay) : base(LFactory, Context, Mapper, PaymentGWay) { }

        #region GetParametersClasses
        public class ListGroupsParameters : JBaseQueryParameters
        {
            /// <summary>
            /// Filtro pelo nome do grupo
            /// </summary>
            public string name { get; set; }

            /// <summary>
            /// Retorna o grupo pelo seu ID
            /// </summary>
            /// <example>12</example>
            public int? group_id { get; set; }

            /// <summary>
            /// Adiciona aos grupos as recorrências atreladas a ele. **Pode ocasionar lentidão em grandes grupos**
            /// </summary>
            public bool add_recurrences { get; set; }
        }

        public class ListRecurrencesParameters : JBaseQueryParameters
        {
            /// <summary>
            /// Filtro pelo nome da recorrência
            /// </summary>
            public string name { get; set; }

            /// <summary>
            /// Filtro pelo id do grupo onde está a recorrência
            /// </summary>
            /// <example>12</example>
            public int? group_id { get; set; }

            /// <summary>
            /// Filtro pelo id da recorrência
            /// </summary>
            /// <example>42</example>
            public int? recurrence_id { get; set; }

            /// <summary>
            /// Retorna somente recorrências ativas
            /// </summary>
            public bool filter_inactive { get; set; } = true;
        }

        public class ListEndUsersParameters : JBaseQueryParameters
        {
            /// <summary>
            /// Filtro pelo nome do usuário
            /// </summary>
            public string name { get; set; }

            /// <summary>
            /// Filtro pelo Id do sistema
            /// </summary>
            public string system_id { get; set; }

            /// <summary>
            /// Filtro pelo e-mail
            /// </summary>
            public string email { get; set; }

            /// <summary>
            /// Filtro pelo telefone
            /// </summary>
            public string phone_number { get; set; }
        }


        public class ListEndUserChargesParameters : JBaseQueryParameters
        {
            /// <summary>
            /// Filtro pelo nome da recorrência
            /// </summary>
            public string name { get; set; }

            /// <summary>
            /// Filtro pelo e-mail
            /// </summary>
            public string email { get; set; }
        }


        #endregion

        /// <summary>
        /// Listar grupos
        /// </summary>
        /// <remarks>Retorna a lista de grupos cadastrados</remarks>
        [HttpGet("list_groups")]
        [ProducesResponseType(typeof(List<GwGroup>), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]        
        public ActionResult ListGroups([FromQuery]ListGroupsParameters Parameters)
        {
            if(!ModelState.IsValid)            
                return BadRequest(GetValidationErros());
            

            DbM_User User = GetUser(Parameters.api_key);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));


            try
            {
                List<GwGroup> Groups = null;

                if (!string.IsNullOrEmpty(Parameters.name))
                    Groups = AtMapper.Map<List<GwGroup>>(User.Groups.Where(x => x.Name.Contains(Parameters.name)));
                else if (Parameters.group_id != null)
                    Groups = AtMapper.Map<List<GwGroup>>(User.Groups.Where(x => x.Id == Parameters.group_id));
                else
                    Groups = AtMapper.Map<List<GwGroup>>(User.Groups); 


                if (Parameters.add_recurrences == true)
                    Groups.ForEach(x => x.SerializeRecurrences = true);

                if(Parameters.paginate)                
                    return Ok(new GwPaginatedResponse<GwGroup>(Groups, Parameters.page, Parameters.count));
                else
                    return Ok(Groups);

            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<GroupController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<GroupController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }

        /// <summary>
        /// Criar um grupo
        /// </summary>
        /// <remarks>Permite a criaçao de um grupo de recorrencias, onde é possível agrupa-las de forma organizada</remarks>
        [HttpPost("create_group")]
        [ProducesResponseType(typeof(GwGroup), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]        
        public async Task<ActionResult> CreateGroup([FromBody] JGwGroup NewGroup)
        {
            DbM_User User = GetUser(NewGroup.ApiKey);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            try
            {
                if (User.Groups.Any(x => x.Name == NewGroup.Name))
                    return BadRequest(new GwModelCreationError(ErrorTypes.AlreadyExists, "já existe um grupo com esse nome", nameof(JGwGroup.Name).ToSnakeCase()));


                DbM_Group Group = SupportMethods.Cast<DbM_Group>(NewGroup);
                User.Groups.Add(Group);
                Group.DateCreated = DateTime.Now.ToUniversalTime();

                await DBContext.SaveChangesAsync();


                return MappedOk<GwGroup>(Group);
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<GroupController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<GroupController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }



        /// <summary>
        /// Criar uma recorrência
        /// </summary>
        /// <remarks>Uma recorrência nada mais é que uma cobrança automatizada. Defina regras, valores e como o usuário final será cobrado, e depois pode reutilizar essa recorrência quantas vezes quiser</remarks>
        [HttpPost("create_recurrence")]
        [ProducesResponseType(typeof(GwRecurrence), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]        
        public async Task<ActionResult> CreateRecurrence([FromBody] JGwRecurrence NewRecurrence)
        {
            DbM_User User = GetUser(NewRecurrence.ApiKey);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            try
            {
                if (User.ContainsRecurrence(NewRecurrence.Name))
                    return BadRequest(new GwModelCreationError(ErrorTypes.AlreadyExists, "já existe uma recorrência com esse nome", nameof(JGwRecurrence.Name).ToSnakeCase()));

                //------------------------------------------------------------------------------------

                DbM_Recurrence Recurrence = AtMapper.Map<DbM_Recurrence>(NewRecurrence);

                List<JGwRecurrenceSplitRule> DiscountSplitsBackup = NewRecurrence.SplitRules.Copy();
                List<JGwRecurrenceSplitRule> FineSplitsBackup = NewRecurrence.SplitRules.Copy();

                //------------------------------------------------------------------------------------
                //------------------------------------------------------------------------------------

                //Verifica se aplicando os descontos, algum recebedor não fica negativo
                List<JGwPaymentRule> DiscountRules = NewRecurrence.PaymentRules.Where(x => x.Type == GwPaymentRuleType.DiscountBeforeExpiration).ToList();

                if (DiscountRules.IsNotNullOrEmpty())
                {
                    try
                    {
                        JGwTransactionCalculation Calculation = CalculateInvoiceDiscountRules(Recurrence);
                        await GetTransactionValues(NewRecurrence.PaymentMethods, Calculation);
                    }
                    catch (GwModelCreationException E)
                    {
                        return BadRequest(E.Error);
                    }
                }

                //------------------------------------------------------------------------------------
                //------------------------------------------------------------------------------------

                //Verifica se aplicando as multas, algum recebedor não fica negativo
                List<JGwPaymentRule> FineRules = NewRecurrence.PaymentRules.Where(x => x.Type == GwPaymentRuleType.DailyFine || x.Type == GwPaymentRuleType.ExpirationFine).ToList();

                if (FineRules.IsNotNullOrEmpty())
                {
                    try
                    {
                        JGwTransactionCalculation Calculation = CalculateInvoiceFineRules(Recurrence);
                        await GetTransactionValues(NewRecurrence.PaymentMethods, Calculation);
                    }
                    catch (GwModelCreationException E)
                    {
                        return BadRequest(E.Error);
                    }
                }

                //------------------------------------------------------------------------------------
                //------------------------------------------------------------------------------------

                JGwTransactionCalculation TPayment = new JGwTransactionCalculation();

                TPayment.Amount = NewRecurrence.Amount;

                try
                {
                    JGwTransactionCalculation Calculation = CalculateInvoiceWithoutRules(Recurrence);
                    await GetTransactionValues(NewRecurrence.PaymentMethods, Calculation);
                }
                catch (GwModelCreationException E)
                {
                    return BadRequest(E.Error);
                }

                Recurrence.DateCreated = DateTime.Now.ToUniversalTime();
                Recurrence.ActivationDate = DateTime.Now.ToUniversalTime();

                User.Recurrences.Add(Recurrence);

                await DBContext.SaveChangesAsync();


                return MappedOk<GwRecurrence>(Recurrence);
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<GroupController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<GroupController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }




        private async Task GetTransactionValues(List<GwPaymentMethod> PaymentMethods, JGwTransactionCalculation TPayment)
        {

            foreach (var PMethod in PaymentMethods)
            {
                TPayment.PaymentMethod = PMethod;
                GwTransactionCalculationValues Values = await PaymentGWay.GetTransactionValues(TPayment, true);


                List<JGwSplitRule> TaxPayers = TPayment.SplitRules.Where(x => x.ChargeProcessingFee == true).ToList();
                double TaxPayersAmount = TaxPayers.Sum(x => x.Amount);

                foreach (var FeeSplit in TaxPayers)
                {
                    double SplitPercentage = (double)FeeSplit.Amount / TaxPayersAmount;
                    double TaxSplitAmount = Values.Tax * SplitPercentage;

                    if (FeeSplit.Amount - TaxSplitAmount < 1)
                        throw new GwModelCreationException(new GwModelCreationError(ErrorTypes.NegativeSplitAmount, "um dos recebedores terá um valor negativo de split calculando todas as taxas e descontos", 
                            FeeSplit.RecipientId + ";" + (FeeSplit.Amount - TaxSplitAmount).ToString()));
                }
            }
        }

        //private async Task<JGwTransaction> PrepareTransaction(GwPaymentMethod PaymentMethod, JGwRecurrence Recurrence, DbM_User User)
        //{
        //    JGwTransaction TPayment = new JGwTransaction();

        //    TPayment.Billing = new JGwBilling();
        //    TPayment.Billing.Name = "transacão GwGroupPayment";

        //    if (Recurrence.PaymentMethod == GwPaymentMethod.Boleto || PayCharge.PaymentMethod == GwPaymentMethod.CreditCard)
        //    {
        //        GwCustomer Customer = await PaymentGWay.CreateCustomer(PayCharge.PayerCustomer, true);
        //        TPayment.Customer = new JGwCustomer() { Id = Customer.Id };

        //        if (PayCharge.PaymentMethod == GwPaymentMethod.CreditCard)
        //        {
        //            PayCharge.PayerCard.CustomerId = Customer.Id;

        //            GwCard NewCard = await PaymentGWay.CreateCard(PayCharge.PayerCard, true);
        //            TPayment.CardId = NewCard.Id.ToString();
        //        }
        //    }


        //    TPayment.PaymentMethod = PayCharge.PaymentMethod;
        //    TPayment.TransactionType = GwTransactionType.Normal;
        //    TPayment.Amount = PayCharge.Amount;

        //    TPayment.Billing.Address = new JGwAddress();
        //    TPayment.Billing.Address.City = PayCharge.PayerCustomer.Address.City;
        //    TPayment.Billing.Address.Country = "br";
        //    TPayment.Billing.Address.Neighborhood = PayCharge.PayerCustomer.Address.Neighborhood;
        //    TPayment.Billing.Address.State = PayCharge.PayerCustomer.Address.State;
        //    TPayment.Billing.Address.Street = PayCharge.PayerCustomer.Address.Street;
        //    TPayment.Billing.Address.StreetNumber = PayCharge.PayerCustomer.Address.StreetNumber;
        //    TPayment.Billing.Address.Zipcode = PayCharge.PayerCustomer.Address.Zipcode;


        //    if (User.Name.Length > 13)
        //        TPayment.SoftDescriptor = User.Name.Substring(0, 13);
        //    else
        //        TPayment.SoftDescriptor = User.Name;


        //    TPayment.SplitRules = TransformRules(Invoice.FinalAmount, Invoice.ChargeNavigation.RecurrenceNavigation.SplitRules);

        //    TPayment.PostbackUrl = DBContext.SystemConfigs.PostbackUrl;

        //    return TPayment;
        //}


        /// <summary>
        /// Editar dados de uma recorrência
        /// </summary>
        /// <remarks>Permite atualizar uma recorrência com novas configurações. Algumas delas não são editáveis, como intervalo</remarks>
        /// <param name="EditedRecurrence"></param>
        [HttpPost("edit_recurrence")]
        [ProducesResponseType(typeof(GwRecurrence), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]        
        public async Task<ActionResult> EditRecurrence([FromBody] JGwEditedRecurrence EditedRecurrence)
        {
            DbM_User User = GetUser(EditedRecurrence.ApiKey);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            try
            {
                DbM_Recurrence Recurrence = User.GetRecurrence(EditedRecurrence.RecurrenceId);

                if (Recurrence == null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.NotFound, "recorrência não encontrada", nameof(JGwEditedRecurrence.RecurrenceId).ToSnakeCase()));

                if (Recurrence.Charges.Any(x => x.SortedInvoices.IsNotNullOrEmpty() && x.SortedInvoices.Any(y => y.PaymentMethod == GwPaymentMethod.Boleto && y.TransactionStatus == GwTransactionStatus.WaitingPayment)))
                    return BadRequest(new GwModelCreationError(ErrorTypes.OpenBoleto, "exitem faturas via boleto abertas. Não é possível alterar a recorrência enquanto as cobranças possuírem faturas via boleto não finalizadas", "-"));


                Recurrence.AllowPaymentAfterExpiration = (bool)EditedRecurrence.AllowPaymentAfterExpiration;
                Recurrence.Amount = (int)EditedRecurrence.Amount;
                Recurrence.Description = EditedRecurrence.Description;
                Recurrence.EndUserComment = EditedRecurrence.EndUserComment;
                Recurrence.Name = EditedRecurrence.Name;
                Recurrence.PaymentMethods = EditedRecurrence.PaymentMethods;

                Recurrence.PaymentRules.Clear();
                Recurrence.PaymentRules = AtMapper.Map<List<DbM_PaymentRule>>(EditedRecurrence.PaymentRules);

                Recurrence.SplitRules = AtMapper.Map<List<GwRecurrenceSplitRule>>(EditedRecurrence.SplitRules);
                Recurrence.Status = (GwRecurrenceStatus)EditedRecurrence.Status;

                Recurrence.DateUpdated = DateTime.Now.ToUniversalTime();

                if(Recurrence.Status == GwRecurrenceStatus.Inactive && EditedRecurrence.Status == GwRecurrenceStatus.Active)
                    Recurrence.ActivationDate = DateTime.Now.ToUniversalTime();


                await DBContext.SaveChangesAsync();


                return MappedOk<GwRecurrence>(Recurrence);
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<GroupController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<GroupController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }

        /// <summary>
        /// Listar recorrências
        /// </summary>
        /// <remarks>Retorna uma lista de recorrências cadastradas</remarks>    
        [HttpGet("list_recurrences")]
        [ProducesResponseType(typeof(List<GwRecurrence>), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]        
        public ActionResult ListRecurrences([FromQuery] ListRecurrencesParameters Parameters)
        {
            if (!ModelState.IsValid)
                return BadRequest(GetValidationErros());

            DbM_User User = GetUser(Parameters.api_key);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));


            try
            {
                List<GwRecurrence> Recurrences = new List<GwRecurrence>();


                if(Parameters.filter_inactive)
                    Recurrences.AddRange(AtMapper.Map<List<GwRecurrence>>(User.Recurrences.Where(x => x.Status == GwRecurrenceStatus.Active)));
                else
                    Recurrences.AddRange(AtMapper.Map<List<GwRecurrence>>(User.Recurrences));

                if (Parameters.recurrence_id != null)
                    Recurrences = Recurrences.Where(x => x.Id == Parameters.recurrence_id).ToList();
                else if (!string.IsNullOrEmpty(Parameters.name))
                    Recurrences = Recurrences.Where(x => x.Name.Contains(Parameters.name)).ToList(); 
                else if(Parameters.group_id != null)
                    Recurrences = Recurrences.Where(x => x.GroupId == Parameters.group_id).ToList();

                Recurrences = Recurrences.OrderByDescending(x => x.DateCreated).ToList();

                if (Parameters.paginate)
                    return Ok(new GwPaginatedResponse<GwRecurrence>(Recurrences, Parameters.page, Parameters.count));
                else
                    return Ok(Recurrences.OrderByDescending(x => x.DateCreated));
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<GroupController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<GroupController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }

        /// <summary>
        /// Atribuir recorrência
        /// </summary>
        /// <remarks>Atribui uma recorrência a um grupo. Dessa forma permite gerir de forma organizada suas recorrências</remarks>
        [ProducesResponseType(typeof(GwGroup), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]
        [HttpPost("assing_recurrence_group")]
        public async Task<ActionResult> AssignRecurrenceGroup([FromBody] JGwARRecurrenceGroup AR)
        {
            DbM_User User = GetUser(AR.ApiKey);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            try
            {
                DbM_Group Group = User.GetGroup(AR.GroupId);

                if (Group == null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.NotFound, "grupo não encontrado", nameof(JGwARRecurrenceGroup.GroupId).ToSnakeCase()));


                DbM_Recurrence Recurrence = User.GetRecurrence(AR.RecurrenceId);

                if (Recurrence == null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.NotFound, "recorrência não encontrada", nameof(JGwARRecurrenceGroup.RecurrenceId).ToSnakeCase()));

                if(Group.MaxItems > 0 && Group.MaxItems <= Group.Recurrences.Count)
                    return BadRequest(new GwModelCreationError(ErrorTypes.GroupFull, "grupo chegou ao limite de recorrências", nameof(GwGroup.MaxItems).ToSnakeCase()));

                if (Group.Recurrences.Any(x => x.Id == Recurrence.Id))
                    return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "grupo já contém essa recorrência", nameof(JGwARRecurrenceGroup.RecurrenceId).ToSnakeCase()));


                Group.Recurrences.Add(Recurrence);

                await DBContext.SaveChangesAsync();


                return MappedOk<GwGroup>(Group);
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<GroupController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<GroupController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }

        /// <summary>
        /// Remover recorrência
        /// </summary>
        /// <remarks>Remove uma recorrência de um grupo</remarks>
        [HttpPost("remove_recurrence_group")]
        [ProducesResponseType(typeof(GwGroup), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]
        public async Task<ActionResult> RemoveRecurrenceGroup([FromBody] JGwARRecurrenceGroup AR)
        {
            DbM_User User = GetUser(AR.ApiKey);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            try
            {
                DbM_Group Group = User.GetGroup(AR.GroupId);

                if (Group == null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.NotFound, "grupo não encontrado", nameof(AR.GroupId).ToSnakeCase()));


                DbM_Recurrence Recurrence = User.GetRecurrence(AR.RecurrenceId);

                if (Recurrence == null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.NotFound, "recorrência não encontrada", nameof(AR.RecurrenceId).ToSnakeCase()));

                if (Recurrence.Charges.IsNotNullOrEmpty())
                    return BadRequest(new GwModelCreationError(ErrorTypes.NotEmpty, "é necessário remover todas as cobranças da recorrência para poder remove-la do grupo", nameof(AR.RecurrenceId).ToSnakeCase()));


                Group.Recurrences.Remove(Recurrence);

                await DBContext.SaveChangesAsync();


                return MappedOk<GwGroup>(Group);
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<GroupController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<GroupController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }

        /// <summary>
        /// Criar usuário final
        /// </summary>
        /// <remarks>Cria um usuário final, que é uma pessoa a ser cobrada de um valor, conforme as regras da recorrência atribuída a ele.</remarks>
        [HttpPost("create_end_user")]
        [ProducesResponseType(typeof(GwEndUser), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]
        public async Task<ActionResult> CreateEndUser([FromBody] JGwEndUser NewEndUser)
        {
            DbM_User User = GetUser(NewEndUser.ApiKey);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            try
            {
                DbM_EndUser EndUser = User.GetEndUserByEmail(NewEndUser.Email);

                if (EndUser != null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.AlreadyExists, "já existe um usuário com esse e-mail", nameof(JGwEndUser.Email).ToSnakeCase()));


                EndUser = User.GetEndUserBySysId(NewEndUser.SystemId);

                if (EndUser != null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.AlreadyExists, "já existe um usuário com esse ID de sistema", nameof(JGwEndUser.SystemId).ToSnakeCase()));


                EndUser = User.GetEndUserByPhone(NewEndUser.PhoneNumber);

                if (EndUser != null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.AlreadyExists, "já existe um usuário com esse telefone", nameof(JGwEndUser.PhoneNumber).ToSnakeCase()));


                DbM_EndUser EUser = SupportMethods.Cast<DbM_EndUser>(NewEndUser);

                EUser.DateCreated = DateTime.Now.ToUniversalTime();

                User.EndUsers.Add(EUser);

                await DBContext.SaveChangesAsync();


                return Ok(EUser.CreateGwModel(AtMapper));
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<GroupController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<GroupController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }

        /// <summary>
        /// Listar usuários finais
        /// </summary>
        [HttpGet("list_end_users")]
        [ProducesResponseType(typeof(List<GwEndUser>), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]        
        public ActionResult ListEndUsers([FromQuery] ListEndUsersParameters Parameters)
        {
            if (!ModelState.IsValid)
                return BadRequest(GetValidationErros());


            DbM_User User = GetUser(Parameters.api_key);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));


            try
            {
                List<DbM_EndUser> Users = new List<DbM_EndUser>();

                if (!string.IsNullOrEmpty(Parameters.name))
                    Users.AddRange(User.GetEndUserByName(Parameters.name));

                else if (!string.IsNullOrEmpty(Parameters.system_id))
                    Users.Add(User.GetEndUserBySysId(Parameters.system_id));

                else if (!string.IsNullOrEmpty(Parameters.email))
                    Users.Add(User.GetEndUserByEmail(Parameters.email));

                else if (!string.IsNullOrEmpty(Parameters.phone_number))
                    Users.Add(User.GetEndUserByPhone(Parameters.phone_number));

                else
                    Users.AddRange(User.EndUsers);


                Users.RemoveAll(x => x == null);


                List<GwEndUser> EUser = new List<GwEndUser>();

                if(Users.IsNotNullOrEmpty())
                {
                    foreach (var Us in Users)
                        EUser.Add(Us.CreateGwModel(AtMapper));
                }


                if (Parameters.paginate)
                    return Ok(new GwPaginatedResponse<GwEndUser> (EUser, Parameters.page, Parameters.count));
                else
                    return Ok(EUser);
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<GroupController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<GroupController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }

        /// <summary>
        /// Listar cobranças usuário
        /// </summary>
        /// <remarks>Retorna as cobranças atreladas aos usuários finais</remarks>
        [HttpGet("list_end_user_charges")]
        [ProducesResponseType(typeof(List<GwEndUserCharge>), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]        
        public ActionResult ListEndUserCharges([FromQuery] ListEndUserChargesParameters Parameters)
        {
            if (!ModelState.IsValid)
                return BadRequest(GetValidationErros());

            DbM_User User = GetUser(Parameters.api_key);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            if (string.IsNullOrEmpty(Parameters.email))
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "variável é obrigatória", "email"));

            try
            {
                List<DbM_Charge> Charges = DBContext.DPCharges.Include(x => x.EndUserNavigation).Include(x => x.RecurrenceNavigation).Where(x => x.EndUserNavigation.Email == Parameters.email).ToList();

                List<GwEndUserCharge> EUserCharges = new List<GwEndUserCharge>();

                Charges.ForEach(x => EUserCharges.Add(x.CreateGwEndUserCharge(AtMapper)));



                EUserCharges = EUserCharges.OrderByDescending(x => x.DateCreated).ToList();

                if (Parameters.paginate)
                    return Ok(new GwPaginatedResponse<GwEndUserCharge>(EUserCharges, Parameters.page, Parameters.count));
                else
                    return Ok(EUserCharges);
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<GroupController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<GroupController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }

        /// <summary>
        /// Criar cobrança
        /// </summary>
        /// <remarks>Uma cobrança é quando um usuário final é atrelado a uma recorrência. Dessa forma, usuando as regras da recorrência, o usuário final é cobrando de uma valor, conformas as regras configuradas</remarks>
        [HttpPost("create_charge")]
        [ProducesResponseType(typeof(GwRecurrence), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]        
        public async Task<ActionResult> CreateCharge([FromBody] JGwCharge Charge)
        {
            DbM_User User = GetUser(Charge.ApiKey);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            try
            {
                DbM_EndUser EndUser = User.GetEndUserById(Charge.EndUserId);

                if (EndUser == null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.NotFound, "usuário final não encontrado", nameof(JGwCharge.EndUserId).ToSnakeCase()));


                DbM_Recurrence Recurrence = User.GetRecurrence(Charge.RecurrenceId);

                if (Recurrence == null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.NotFound, "recurrência não encontrada", nameof(JGwCharge.RecurrenceId).ToSnakeCase()));


                DbM_Charge NewCharge = Recurrence.GetChargeByEUserId((int)Charge.EndUserId);

                if (NewCharge != null)
                    return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "já existe uma cobrança para esse usuário com essa recorrência", nameof(JGwCharge.RecurrenceId).ToSnakeCase()));


                NewCharge = new DbM_Charge();
                NewCharge.RecurrenceId = (int)Charge.RecurrenceId;
                NewCharge.EndUserId = (int)Charge.EndUserId;
                NewCharge.DateCreated = DateTime.Now.ToUniversalTime();
                
                Recurrence.Charges.Add(NewCharge);

                await DBContext.SaveChangesAsync();

                NewCharge.UpdateNextExpiration(DBContext.DPHolidays.ToList());

                await DBContext.SaveChangesAsync();

                return MappedOk<GwRecurrence>(Recurrence);
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<GroupController>(E);

                return BadRequest(E.Error);
            }
            catch (Exception E)
            {
                ExceptionController.LogError<GroupController>(E);

                return BadRequest(GwModelCreationError.GetDefaultError());
            }
        }
    }
}
