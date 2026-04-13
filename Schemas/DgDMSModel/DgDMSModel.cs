using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DgIntegration.DgDMS
{
	public class DMSErrorResponse
	{
        public string code { get; set; }
		public string message { get; set; }
		public string reason { get; set; }
		
		public string GetMessage()
		{
			return $"{code}: {message}. {reason}";
		}
		
		public override string ToString()
		{
			return JsonConvert.SerializeObject(this, Formatting.Indented);
		}
    }

    public abstract class DMSModelConverter<T> : JsonConverter
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
		
		public bool IsErrorResponse(JObject jObject)
		{
			return jObject.ContainsKey("code") && jObject.ContainsKey("message") && jObject.ContainsKey("reason");
		}
    }
}