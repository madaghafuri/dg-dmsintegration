using System;
using System.Collections.Generic;
using DgIntegration.DgDMS;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DgIntegration.DgDMS
{
    public interface IDealerResponse {}

    #region SuccessResponse

    public class DealerSuccessResponse : IDealerResponse
    {
        public string id { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string href { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool isLegalEntity { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string isHeadOffice { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string organizationType { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string name { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public organizationParentRelationship organizationParentRelationship { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string tradingName { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string nameType { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string status { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<externalReference> externalReference { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<PartyCharacteristic> partyCharacteristic { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<DealerRelatedParty> relatedParty { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public contact contact { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public existsDuring existsDuring { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<places> places { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<products> products { get; set; }
    }

    public class organizationParentRelationship
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string relationshipType { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public organization organization { get; set; }
    }

    public class organization
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string id { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string href { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string name { get; set; }
    }

    public class externalReference
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string name { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string externalIdentifierType { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string @type { get; set; }
    }

    public class PartyCharacteristic
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string name { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string value { get; set; }
    }

    public class DealerRelatedParty
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string role { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string type { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public object partyOrPartyRole { get; set; }
    }

    public class contact
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<BaseContactMedium> contactMedium { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public preferredContact preferredContact { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public object validFor { get; set; }
    }
    
    public class preferredContact
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string preferredContactType { get; set; }
    }

    public class BaseContactMedium 
    {
        public string baseType { get; set; }
        public string contactType { get; set; }
        public string type { get; set; }

        #region EmailContactMedium Field
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string emailAddress { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string faxNumber { get; set;}
        #endregion

        #region GeographicContactMedium Field
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string city { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string country { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string postCode { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string stateOrProvince { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string street1 { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string street2 { get; set; }
        #endregion

        #region PhoneContactMedium Field
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string phoneNumber { get; set; }
        #endregion
    }

    public class existsDuring
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string endDateTime { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string startDateTime { get; set; }
    }

    public class places
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string id { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string name { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string type { get; set; }
    }

    public class products
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string id { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string name { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<subProducts> subProducts { get; set; }
    }

    public class subProducts
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string name { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string type { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string partNumber { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string membershipType { get; set; }
    }

    #endregion

    #region ErrorResponse

    public class DealerErrorResponse: IDealerResponse {
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

    #endregion

    public class DealerResponseConverter: JsonConverter<IDealerResponse>
    {
        public bool IsErrorResponse(JObject jObject)
		{
			return jObject.ContainsKey("code") && jObject.ContainsKey("message") && jObject.ContainsKey("reason");
		}

        protected IDealerResponse Create(Type objectType, JObject jObject)
        {
            if (IsErrorResponse(jObject)) {
                return new DealerErrorResponse();
            }

            return new DealerSuccessResponse();
        }

        public override IDealerResponse ReadJson(JsonReader reader, Type objectType, IDealerResponse existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jObject = JObject.Load(reader);
            var target = Create(objectType, jObject);
            serializer.Populate(jObject.CreateReader(), target);

            return target;
        }

        public override void WriteJson(JsonWriter writer, IDealerResponse value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }
}