using Microsoft.OpenApi.Models;
using lk.Shared.Utilities;
using lk.Shared.Utilities.Extensions;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace lk.Server.GroupPaymentGateway.Utilities
{
    /// <summary>
    /// Edita o documento Swagger para ignorar as propriedades marcadas com o atributo SwaggerIgnoreAttribute
    /// </summary>
    public class SwaggerIgnoreFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema Schema, SchemaFilterContext FilterContext)
        {
            if (Schema.Properties.Count == 0)
                return;

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var MemberList = FilterContext.Type.GetFields(Flags).Cast<MemberInfo>().Concat(FilterContext.Type.GetProperties(Flags));

            var MemberProps = MemberList.Where(m => m.GetCustomAttribute<SwaggerIgnoreAttribute>() != null).Select(m => (m.GetCustomAttribute<SwaggerIgnoreAttribute>().PropertyName ?? m.Name.ToSnakeCase()));

            foreach (var ExName in MemberProps)
            {
                if (Schema.Properties.ContainsKey(ExName.ToSnakeCase()))
                    Schema.Properties.Remove(ExName.ToSnakeCase());
            }
        }
    }
}