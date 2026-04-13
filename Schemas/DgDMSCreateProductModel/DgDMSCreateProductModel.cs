using System;
using System.Collections.Generic;
using DgIntegration.DgDMS;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DgIntegration.DgDMS
{
    public class CreateProductRequest
    {
        public List<CPExternalReference> externalReference { get; set; }
        public string completionDate { get; set; }
        public List<productOrderItem> productOrderItem { get; set; }
        public List<CPRelatedParty> relatedParty { get; set; }
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<Note> note { get; set; }
    }

    public class CPExternalReference
    {
        public string externalIdentifierType { get; set; }
        public string id { get; set; }
    }

    public class productOrderItem
    {
        public string id { get; set; }
        public string reservationId { get; set; }
        public string action { get; set; }
        public List<productOrderLineItem> productOrderLineItem { get; set; }
        public int quantity { get; set; }
    }

    public class CPRelatedParty
    {
        public string name { get; set; }
        public string id { get; set; }
        public string role { get; set; }
        public List<contactMedium> contactMedium { get; set; }
    }

    public class productOrderLineItem
    {
        public string id { get; set; }
        public string action { get; set; }
        public int quantity { get; set; }
        public product product { get; set; }
    }

    public class product
    {
        public string id { get; set; }
        public string name { get; set; }
    }

    public class contactMedium
    {
        public string mediumType { get; set; }
        public characteristic characteristic { get; set; }
    }

    public class characteristic
    {
        public string streetAddress1 { get; set; }
        public string streetAddress2 { get; set; }
        public string postcode { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string country { get; set; }
        public string emailAddress { get; set; }
        public string faxNumber { get; set; }
        public string phoneNumber { get; set; }
    }
	
	public class Note
    {
        public string id { get; set; }
        public string text { get; set; }
    }

    #region Response

    public interface ICreateProductResponse {}

    public class CreateProductSuccessResponse: ICreateProductResponse
    {
        public string id { get; set; }
        public List<externalReference> externalReference { get; set; }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }

    public class CreateProductErrorResponse: DMSErrorResponse, ICreateProductResponse {}

    public class CreateProductResponseConverter: DMSModelConverter<ICreateProductResponse>
    {
        protected override ICreateProductResponse Create(Type objectType, JObject jObject)
        {
            if (IsErrorResponse(jObject)) {
                return new CreateProductErrorResponse();
            }

            return new CreateProductSuccessResponse();
        }
    }

    #endregion
}