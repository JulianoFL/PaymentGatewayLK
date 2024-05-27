
using AutoMapper;

namespace lk.Server.GroupPaymentGateway.Models.DBModels
{
    public abstract class BaseDbM 
    {
        public BaseDbM() { }
        public BaseDbM(Action<object, string> lazyLoader)
        {
            LazyLoader = lazyLoader;
        }


        internal Action<object, string> LazyLoader { get; set; }

        //public T CreateGwModel<T>(IMapper AtMapper)
        //{
        //    return AtMapper.Map<this, GwEndUser>(this);

        //    return (T)AtMapper.Map(this, this.GetType(), typeof(T));
        //}
    }
}
