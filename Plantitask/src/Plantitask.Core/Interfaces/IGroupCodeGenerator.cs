using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.Interfaces
{
    public interface IGroupCodeGenerator
    {
        string Generate(string groupName);
        bool IsValid(string code);
    }
}
