using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DgIntegration.DgDMS
{
    #region Request
	
	public class ReserveStockRequest
	{
		public string reservationId { get; set; }
        public place place { get; set; }
        public List<reserveProductStockItem> reserveProductStockItem { get; set; }
        public string isPartialReservationAllowed { get; set; }
        public string reserveProductStockState { get; set; }
		
		public override string ToString()
		{
			return JsonConvert.SerializeObject(this, Formatting.Indented);
		}
	}
    
    public class reserveProductStockItem
    {
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string isAllocateSerials { get; set; }
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<productStockReserved> productStockReserved { get; set; }
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
		public List<ProductSerialNumbers> productSerialNumbers { get; set; }
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public quantity quantityRequested { get; set; }
    }

    public class productStockReserved
    {
        public string id { get; set; }
        public string type { get; set; }
    }

	public class ProductSerialNumbers
	{
		public List<string> listOfSerialNumbers { get; set; }
	}

    #endregion

	#region Response

	public interface IReserveStockResponse { }
	
	public class ReserveStockSuccessResponse : IReserveStockResponse
	{
		public string productStockStatusType { get; set; }
		public string reservationId { get; set; }
		public place place { get; set; }
		public List<relatedParty> relatedParty { get; set; }
		public List<reservedProductStockItem> reservedProductStockItem { get; set; }
		public override string ToString()
		{
			return JsonConvert.SerializeObject(this, Formatting.Indented);
		}
	}
	
	public class reservedProductStockItem
	{
		public productStockReserved productStockReserved { get; set; }
		public List<productSerialNumbers> productSerialNumbers { get; set; }
		public quantity quantityReserved { get; set; }
		public productOffering productOffering { get; set; }
	}
	
	public class productSerialNumbers
	{
		public List<string> listOfserialNumbers { get; set; }
	}
	
	public class ReserveStockErrorResponse : DMSErrorResponse, IReserveStockResponse {}
	
	public class ReserveStockResponseConverter : DMSModelConverter<IReserveStockResponse>
    {
        protected override IReserveStockResponse Create(Type objectType, JObject jObject)
        {	
			if(IsErrorResponse(jObject)) {
				return new ReserveStockErrorResponse();
			}

            return new ReserveStockSuccessResponse();
        }
    }
	
	#endregion
}