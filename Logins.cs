using System;
using System.Collections.Generic;

namespace paymenu.Server.PaymenuGateway
{
    public partial class Logins
    {
        public int Id { get; set; }
        public string ApiKey { get; set; }
        public string Email { get; set; }
    }
}
