﻿using System;
using System.Reflection;
using MySoft.IoC;

namespace MySoft.RESTful.Business.Register
{
    /// <summary>
    /// 代理委托
    /// </summary>
    public class ProxyInvocationHandler : IProxyInvocationHandler
    {
        private Type serviceType;
        public ProxyInvocationHandler(Type serviceType)
        {
            this.serviceType = serviceType;
        }

        #region IProxyInvocationHandler 成员

        /// <summary>
        /// 委托调用
        /// </summary>
        /// <param name="proxy"></param>
        /// <param name="method"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public object Invoke(object proxy, MethodInfo method, object[] parameters)
        {
            var info = CoreHelper.GetMemberAttribute<PublishMethodAttribute>(method);
            if (info != null)
            {
                var instance = CastleFactory.Create();
                var service = instance.GetType().GetMethod("GetChannel", Type.EmptyTypes)
                    .MakeGenericMethod(serviceType).Invoke(instance, null);

                return DynamicCalls.GetMethodInvoker(method).Invoke(service, parameters);
            }
            else
            {
                //定义异常
                Exception ex = new ArgumentException("Method - 【" + method.Name
                       + "】 must be an method marked with PublishMethodAttribute.");

                throw ex;
            }
        }

        #endregion
    }
}
