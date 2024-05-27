using Newtonsoft.Json.Linq;
using lk.Server.Shared;

namespace lk.Server.GroupPaymentGateway.Services
{
    public partial class EmailController : Shared.Services.EmailController
    {
        private GatewayDBContext DBContext { get; set; }


        public EmailController(GatewayDBContext DBContext)
        {
            this.DBContext = DBContext;


            Configure(DBContext.SystemConfigs.SendgridApi);
        }



        public async Task<bool> SendApproachingExpiration(string ReceiverEmail, string ReceiverName, string RecurrenceName, string RecurrenceUrl, string UserName, string EndUserName, DateTime ExpirationDate)
        {
            JObject TemplateData = new JObject();

            TemplateData.Add("pre_cabecalho", "Assinatura " + RecurrenceName); //pre-cabeçalho deve ser o primeiro a ir
            TemplateData.Add("nome_usuario", EndUserName);
            TemplateData.Add("url_assinatura", RecurrenceUrl);
            TemplateData.Add("nome_recorrencia", RecurrenceName);            
            TemplateData.Add("nome_empresa", UserName);
            TemplateData.Add("data_vencimento", ExpirationDate.ToString("dd/MM/yyyy"));
            TemplateData.Add("data_completa", DateTime.Now.ToString("dd/MMM/yyyy"));
            

            return await SendEmail(ReceiverEmail, ReceiverName, "notificacoes@paymenu.com.br", "d-483f37ab49324c598cfd49d587c5383c", TemplateData);
        }
    }
}
