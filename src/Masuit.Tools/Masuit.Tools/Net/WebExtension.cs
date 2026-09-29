using System.Runtime.Remoting.Messaging;
using System.Web;
using System.Web.SessionState;

namespace Masuit.Tools.Net
{
    /// <summary>
    /// Web操作扩展
    /// </summary>
    public static class WebExtension
    {
        #region 获取线程内唯一上下文对象

        /// <summary>
        /// 获取线程内唯一的上下文对象
        /// </summary>
        /// <typeparam name="T">上下文容器对象</typeparam>
        /// <returns>上下文容器对象</returns>
        public static T GetDbContext<T>() where T : new()
        {
            T db;
            if (CallContext.GetData("db") == null) //由于CallContext比HttpContext先存在，所以首选CallContext为线程内唯一对象
            {
                db = new T();
                CallContext.SetData("db", db);
            }

            db = (T)CallContext.GetData("db");
            return db;
        }

        #endregion

        #region 写Session

        /// <summary>
        /// 写Session
        /// </summary>
        /// <param name="session"></param>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        public static void Set(this HttpSessionStateBase session, string key, dynamic value) => session[key] = value;

        /// <summary>
        /// 写Session
        /// </summary>
        /// <param name="session"></param>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        public static void Set(this HttpSessionState session, string key, dynamic value) => session[key] = value;

        #endregion

        #region 获取Session

        /// <summary>
        /// 获取Session
        /// </summary>
        /// <typeparam name="T">对象</typeparam>
        /// <param name="session"></param>
        /// <param name="key">键</param>
        /// <returns>对象</returns>
        public static T Get<T>(this HttpSessionStateBase session, string key) => (T)session[key];

        /// <summary>
        /// 获取Session
        /// </summary>
        /// <typeparam name="T">对象</typeparam>
        /// <param name="session"></param>
        /// <param name="key">键</param>
        /// <returns>对象</returns>
        public static T Get<T>(this HttpSessionState session, string key) => (T)session[key];

        #endregion
    }
}