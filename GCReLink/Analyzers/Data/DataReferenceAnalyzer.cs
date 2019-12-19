using System;
using System.Collections.Generic;
using System.Text;

namespace GCReLink.Analyzers.Data
{
    public static class DataReferenceAnalyzer
    {
        private static RegisterContext[] registerContexts = new RegisterContext[32];

        static DataReferenceAnalyzer()
        {
            for (var i = 0; i < 32; i++)
                registerContexts[i] = new RegisterContext((Register)i);
        }

        //public static List<>
    }
}
