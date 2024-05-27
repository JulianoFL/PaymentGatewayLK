
using AutoMapper;

namespace lk.Server.GroupPaymentGateway.Controllers
{
    [Route("v1/gateway/recipients", Name = "Rotas de recebedores")]    
    public class RecipientsController(ILoggerFactory LFactory, GatewayDBContext Context, IMapper Mapper, PaymentGatewayService PaymentGWay) : BaseController(LFactory, Context, Mapper, PaymentGWay)
    {
        /// <summary>
        /// Listar recebedores
        /// </summary>
        /// <remarks>Retorna a lista de recebedores cadastrados</remarks>
        /// <param name="api_key" example="ur_sua-chave01">Chave da API</param>
        [ProducesResponseType(typeof(List<GwRecipient>), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]
        [HttpGet("[action]"), ActionName("list")]
        public async Task<ActionResult> ListRecipients([FromQuery][Required] string api_key)
        {
            DbM_User User = GetUser(api_key);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));


            try
            {
                return Ok(await PaymentGWay.GetRecipients(User.Id.ToString()));
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<RecipientsController>(E);

                return BadRequest(E.Error);
            }
        }

        /// <summary>
        /// Criar recebedor
        /// </summary>
        ///<remarks>Permite a criação de um recebedor, que poderá ser uma pessoa a receber valores</remarks>
        [ProducesResponseType(typeof(GwRecipient), 200)]
        [ProducesResponseType(typeof(GwModelCreationError), 400)]
        [HttpPost("[action]"), ActionName("create")]
        public async Task<ActionResult> CreateRecipient([FromBody] JGwRecipient NewRecipient)
        {
            DbM_User User = GetUser(NewRecipient.ApiKey);
            if (User == null)
                return BadRequest(new GwModelCreationError(ErrorTypes.InvalidData, "Chave inválida", "api_key"));

            try
            {
                NewRecipient.RefId = User.Id.ToString();

                GwRecipient Recipient = await PaymentGWay.CreateRecipient(NewRecipient);

                return Ok(Recipient);
            }
            catch (GwModelCreationException E)
            {
                ExceptionController.LogWarning<RecipientsController>(E);

                return BadRequest(E.Error);
            }
        }
    }
}