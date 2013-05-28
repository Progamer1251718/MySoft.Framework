using System;
using System.Collections.Generic;
using System.Diagnostics;
using MySoft.IoC.Configuration;
using MySoft.IoC.Messages;
using MySoft.IoC.Services;
using MySoft.Logger;

namespace MySoft.IoC
{
    /// <summary>
    /// The base impl class of the service interface, this class is used by service factory to emit service interface impl automatically at runtime.
    /// </summary>
    internal class ServiceInvocationHandler<T> : IProxyInvocationHandler
    {
        private CastleFactoryConfiguration config;
        private IDictionary<string, int> cacheTimes;
        private IDictionary<string, string> errors;
        private IContainer container;
        private IService service;
        private AsyncCaller caller;
        private IServiceCall call;
        private ILog logger;
        private string hostName;
        private string ipAddress;

        /// <summary>
        ///  Initializes a new instance of the <see cref="ServiceInvocationHandler"/> class.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="container"></param>
        /// <param name="service"></param>
        /// <param name="caller"></param>
        public ServiceInvocationHandler(CastleFactoryConfiguration config, IContainer container, IService service, AsyncCaller caller, IServiceCall call, ILog logger)
        {
            this.config = config;
            this.container = container;
            this.service = service;
            this.call = call;
            this.caller = caller;
            this.logger = logger;

            this.hostName = DnsHelper.GetHostName();
            this.ipAddress = DnsHelper.GetIPAddress();

            this.cacheTimes = new Dictionary<string, int>();
            this.errors = new Dictionary<string, string>();

            var methods = CoreHelper.GetMethodsFromType(typeof(T));
            foreach (var method in methods)
            {
                var contract = CoreHelper.GetMemberAttribute<OperationContractAttribute>(method);
                if (contract != null)
                {
                    if (contract.CacheTime > 0)
                        cacheTimes[method.ToString()] = contract.CacheTime;

                    if (!string.IsNullOrEmpty(contract.ErrorMessage))
                        errors[method.ToString()] = contract.ErrorMessage;
                }
            }
        }

        #region IInvocationHandler 成员

        /// <summary>
        /// 响应委托
        /// </summary>
        /// <param name="proxy"></param>
        /// <param name="method"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public object Invoke(object proxy, System.Reflection.MethodInfo method, object[] parameters)
        {
            #region 设置请求信息

            var collection = IoCHelper.CreateParameters(method, parameters);

            var reqMsg = new RequestMessage
            {
                InvokeMethod = false,
                AppVersion = ServiceConfig.CURRENT_FRAMEWORK_VERSION,       //版本号
                AppName = config.AppName,                                   //应用名称
                AppPath = AppDomain.CurrentDomain.BaseDirectory,            //应用路径
                HostName = hostName,                                        //客户端名称
                IPAddress = ipAddress,                                      //客户端IP地址
                ServiceName = typeof(T).FullName,                           //服务名称
                MethodName = method.ToString(),                             //方法名称
                EnableCache = config.EnableCache,                           //是否缓存
                TransactionId = Guid.NewGuid(),                             //传输ID号
                MethodInfo = method,                                        //设置调用方法
                Parameters = collection,                                    //设置参数
                RespType = ResponseType.Binary                              //数据类型
            };

            //设置缓存时间
            if (cacheTimes.ContainsKey(method.ToString()))
            {
                reqMsg.CacheTime = cacheTimes[method.ToString()];
            }

            #endregion

            //定义返回值
            object returnValue = null;

            //调用方法
            var resMsg = InvokeRequest(reqMsg);

            if (resMsg != null)
            {
                returnValue = resMsg.Value;

                //处理参数
                IoCHelper.SetRefParameters(method, resMsg.Parameters, parameters);
            }

            //返回结果
            return returnValue;
        }

        /// <summary>
        /// Calls the service.
        /// </summary>
        /// <param name="reqMsg">Name of the sub service.</param>
        /// <returns>The result.</returns>
        private ResponseMessage InvokeRequest(RequestMessage reqMsg)
        {
            try
            {
                //定义响应的消息
                var resMsg = GetResponse(reqMsg);

                //如果有异常，向外抛出
                if (resMsg.IsError)
                {
                    if (logger != null) logger.WriteError(resMsg.Error);

                    throw resMsg.Error;
                }

                return resMsg;
            }
            catch (BusinessException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (config.ThrowError)
                {
                    //判断是否有自定义异常
                    if (errors.ContainsKey(reqMsg.MethodName))
                        throw new BusinessException(errors[reqMsg.MethodName]);
                    else
                        throw;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取响应信息
        /// </summary>
        /// <param name="reqMsg"></param>
        /// <returns></returns>
        private ResponseMessage GetResponse(RequestMessage reqMsg)
        {
            //开始一个记时器
            var watch = Stopwatch.StartNew();

            try
            {
                //写日志开始
                call.BeginCall(reqMsg);

                //获取上下文
                var context = GetOperationContext(reqMsg);

                //异步调用服务
                var item = caller.Run(service, context, reqMsg);

                var resMsg = item.Message;

                if (item.Message == null)
                {
                    //反序列化对象
                    resMsg = IoCHelper.DeserializeObject(item.Buffer);
                }

                //设置耗时时间
                resMsg.ElapsedTime = Math.Min(resMsg.ElapsedTime, watch.ElapsedMilliseconds);

                //写日志结束
                call.EndCall(reqMsg, resMsg, watch.ElapsedMilliseconds);

                return resMsg;
            }
            finally
            {
                if (watch.IsRunning)
                {
                    watch.Stop();
                }
            }
        }

        /// <summary>
        /// 获取上下文对象
        /// </summary>
        /// <param name="reqMsg"></param>
        /// <returns></returns>
        private OperationContext GetOperationContext(RequestMessage reqMsg)
        {
            var caller = new AppCaller
            {
                AppVersion = reqMsg.AppVersion,
                AppPath = reqMsg.AppPath,
                AppName = reqMsg.AppName,
                IPAddress = reqMsg.IPAddress,
                HostName = reqMsg.HostName,
                ServiceName = reqMsg.ServiceName,
                MethodName = reqMsg.MethodName,
                Parameters = reqMsg.Parameters.ToString(),
                CallTime = DateTime.Now
            };

            return new OperationContext
            {
                Container = container,
                Caller = caller
            };
        }

        #endregion
    }
}