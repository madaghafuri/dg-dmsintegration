using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.ServiceModel.Activation;
using System.Data;
using System.Data.SqlClient;
using Terrasoft.Configuration;
using Terrasoft.Core;
using Terrasoft.Core.Store;
using Terrasoft.Core.DB;
using Terrasoft.Common;
using Terrasoft.Web.Common;
using Terrasoft.Web.Http.Abstractions;
using Newtonsoft.Json;
using ISAIntegrationSetup;
using SysSettings = Terrasoft.Core.Configuration.SysSettings;

namespace DgIntegration.DgDMS
{
    public class Token
    {
        protected UserConnection UserConnection;
        public string SessionKey = "DMS-TOKEN";

        public Token(UserConnection UserConnection)
        {
            this.UserConnection = UserConnection;
        }

        protected bool isExpired(DateTime time, int expire)
        {
            return DateTime.Compare(time.AddSeconds(expire - 5), DateTime.UtcNow) <= 0;
        }

        public string GetCacheToken()
        {
            var tokenCache = UserConnection.SessionData.GetValue<string>(this.SessionKey);
			if(string.IsNullOrEmpty(tokenCache)) {
				return null;
			}
            
			var tokenData = JsonConvert.DeserializeObject<Dictionary<string, string>>(tokenCache);
			var time = DateTime.Parse(tokenData["modified_at"]); 
            var expire = int.Parse(tokenData["expires_in"]);
			if(isExpired(time, expire)) {
				return null;
			}
			
            return tokenData["access_token"];
        }

        public void UpdateCacheToken(string Token, long Expire)
        {
			var tokenData = new Dictionary<string, string>();
            tokenData["access_token"] = Token;
            tokenData["modified_at"] = DateTime.UtcNow.ToString();
            tokenData["expires_in"] = Expire.ToString();

            UserConnection.SessionData[this.SessionKey] = JsonConvert.SerializeObject(tokenData);
        }
		
		public virtual TokenRequest GetParam(string ClientId, string ClientSecret, string GrantType)
        {
            if(string.IsNullOrEmpty(ClientId)) {
                throw new Exception("Client Id cannot be null or empty");
            }

            if(string.IsNullOrEmpty(ClientSecret)) {
                throw new Exception("Client Secret cannot be null or empty");
            }

            if(string.IsNullOrEmpty(GrantType)) {
                throw new Exception("Grant Type cannot be null or empty");
            }

            return new TokenRequest() {
                client_id = ClientId,
                client_secret = ClientSecret,
                grant_type = GrantType
            };
        }

        public virtual string RandomRequestID()
        {
            Random random = new Random();
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            char[] stringChars = new char[10];

            for (int i = 0; i < 10; i++)
            {
                stringChars[i] = chars[random.Next(chars.Length)];
            }

            return "NCCF" +  new string(stringChars);
        }
    }
}