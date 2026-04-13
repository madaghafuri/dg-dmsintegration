using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DgIntegration.DgDMS
{
    #region Token
        
    public class TokenRequest
    {
        public string client_id { get; set; }
        public string client_secret { get; set; }
        public string grant_type { get; set; }
		
		public Dictionary<string, string> ToDictionary()
		{
			return new Dictionary<string, string>() {
				{"client_id", this.client_id},
				{"client_secret", this.client_secret},
				{"grant_type", this.grant_type}
			};
		}
    }

    public class TokenSuccessResponse: TokenResponse
    {
        public string access_token { get; set; }
        public string scope { get; set; }
        public string token_type { get; set; }
        public long expires_in { get; set; }        
        public long refresh_expires_in { get; set; }
        [JsonProperty("not-before-policy")]
        public long notBeforePolicy { get; set; }
        [JsonProperty("X-Default-BE-ID")]
        public string defaultBeId { get; set; }
        [JsonProperty("X-Source-System-ID")]
        public string sourceSystemId { get; set; }
        [JsonProperty("X-OP-ID")]
        public string opId { get; set; }
        [JsonProperty("X-Allowed-BE-ID")]
        public List<string> allowedBeId { get; set; }
    }

    public class TokenErrorResponse: TokenResponse
    {
        public string error_description { get; set; }
        public string error { get; set; }
    }

    public class TokenResponse {}

    public class TokenResponseConverter: JsonCreationConverter<TokenResponse>
    {
        protected override TokenResponse Create(Type objectType, JObject jObject)
        {
            if (jObject.ContainsKey("access_token")) {
                return new TokenSuccessResponse();
            }

            if (jObject.ContainsKey("expires_in")) {
                return new TokenSuccessResponse();
            }

            if (jObject.ContainsKey("error")) {
                return new TokenErrorResponse();
            }

            return new TokenResponse();
        }
    }

    public abstract class JsonCreationConverter<T> : JsonConverter
    {
        protected abstract T Create(Type objectType, JObject jObject);

        public override bool CanConvert(Type objectType)
        {
            return typeof(T).IsAssignableFrom(objectType);
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject jObject = JObject.Load(reader);
            T target = Create(objectType, jObject);
            serializer.Populate(jObject.CreateReader(), target);

            return target;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }

    #endregion
	
	#region TokenSessionCache
	
	public class TokenSessionCache
    {
        public string access_token { get; set; }
        public string timestamp { get; set; }
        public string expires_in { get; set; }
    }
	
	#endregion
}