
using AutoMapper;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace lk.Server.GroupPaymentGateway.Models.DBModels
{
    public partial class DbM_Recipient
    {
        public string Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int MaxItems { get; set; }
        public DateTime DateCreated { get; set; }


        public virtual DbM_User UserNavigation { get; set; }

        public virtual List<DbM_Recurrence> Recurrences { get; set; }

        public bool ContainsRecurrence(string RecurrenceName) => Recurrences.Any(x => x.Name == RecurrenceName);
    }

}
