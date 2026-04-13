using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DgIntegration.DgDMS
{
    #region Request
        
    public class CheckStockRequest
    {
        public place place { get; set; }
        public List<queryProductStockItem> queryProductStockItem { get; set; }
		
		public override string ToString()
		{
			return JsonConvert.SerializeObject(this, Formatting.Indented);
		}
    }
	
	public class place
    {
        public string id { get; set; }
        public string type { get; set; }
    }
	
	public class queryProductStockItem
    {
        public productSpecification productSpecification { get; set; }
    }
	
	public class productSpecification
	{
		public string id {get; set;}
		public string name {get; set;}
	}
	
	#endregion
	
	#region Response
	
	public interface ICheckStockResponse {}
	
	public class CheckStockSuccessResponse : ICheckStockResponse
	{
    	public place place { get; set; }
    	public List<relatedParty> relatedParty { get; set; }
		public List<queryProductStockItemResponse> queryProductStockItem { get; set; }
		
		public override string ToString()
		{
			return JsonConvert.SerializeObject(this, Formatting.Indented);
		}
		
		public bool IsAvailable(string deviceItem, int qty)
		{
			int available = this.queryProductStockItem
				.Where(item => item.productSpecification.id == deviceItem)
				.Select(item => item.quantityAvailable.amount)
				.FirstOrDefault();
			
			return available >= qty;
		}
	}
	
	public class relatedParty 
	{
		public string id { get;set; }
		public string type { get; set; }
	}
	
	public class queryProductStockItemResponse 
	{
		public List<productCharacteristic> productCharacteristics { get; set; }
		public productSpecification productSpecification { get; set; }
		public quantity quantityReserved { get; set; }
		public quantity quantityAvailable { get; set; }
		public quantity quantityUnavailable { get; set; }
		public quantity inTransitQty { get; set; }
		public productOffering productOffering { get; set; }
		public List<productOfferingPrice> productOfferingPrice { get; set; }
	}

    public class productCharacteristic 
	{
		public string name { get; set; }
		public string value { get; set; }
	}

	public class quantity 
	{
		public int amount { get; set; }
	}

	public class productOfferingPrice 
	{
		public string priceType { get; set; }
		public price price { get; set; }
	}

	public class price 
	{
		public string value { get; set; }
	}

	public class productOffering 
	{
		public string id { get; set; }
	}
	
	public class CheckStockErrorResponse : DMSErrorResponse, ICheckStockResponse {}
	
	public class CheckStockResponseConverter : DMSModelConverter<ICheckStockResponse>
    {
        protected override ICheckStockResponse Create(Type objectType, JObject jObject)
        {	
			if(IsErrorResponse(jObject)) {
				return new CheckStockErrorResponse();
			}

            return new CheckStockSuccessResponse();
        }
    }
	
	#endregion
}