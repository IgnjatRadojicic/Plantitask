using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Infrastructure.Data
{
    public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter() : base (
             // C# -> DB (write): guarantee Kind=Utc so Npgsql accepts it
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            // DB -> C# (read): Npgsql already returns Utc; stamp defensively so it's never Unspecified
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
        )
        { }
    }
}
