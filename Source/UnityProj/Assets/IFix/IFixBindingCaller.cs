using System.Reflection;

namespace IFix
{
    public class IFixBindingCaller
    {
        protected MethodBase method;
        
        public IFixBindingCaller(MethodBase method)
        {
            this.method = method;
        }
    }
}