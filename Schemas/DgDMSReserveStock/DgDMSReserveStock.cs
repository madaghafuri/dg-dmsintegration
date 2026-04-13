using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.ServiceModel.Activation;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrasoft.Configuration;
using Terrasoft.Core;
using Terrasoft.Core.DB;
using Terrasoft.Core.Entities;
using Terrasoft.Common;
using Terrasoft.Web.Common;
using Terrasoft.Web.Http.Abstractions;
using Newtonsoft.Json;

namespace DgIntegration.DgDMS
{
    public class ReserveStock
    {
        protected UserConnection UserConnection;
		protected string reserveProductStockState = "Reserve";

        public ReserveStock(UserConnection UserConnection)
        {
            this.UserConnection = UserConnection;
        }

        public virtual ReserveStockRequest GetParam(string StoreId, List<string> DeviceIds, string ReservationId = "")
        {
            Dictionary<string, int> reservedProductStockItems = DeviceIds
				.GroupBy(id => id)
				.ToDictionary(val => val.Key, val => val.Count());
			
			var param = new ReserveStockRequest();
            param.reservationId = string.IsNullOrEmpty(ReservationId) ? GenerateReferenceId() : ReservationId;
            param.place = new place() {
                id = StoreId,
                type = "Site"
            };
            List<reserveProductStockItem> reserveStockProduct = new List<reserveProductStockItem>();
            foreach (var product in reservedProductStockItems) {
                reserveStockProduct.Add(new reserveProductStockItem() {
                    isAllocateSerials = "false",
                    quantityRequested = new quantity() {
                        amount = product.Value
                    },
                    productStockReserved = new List<productStockReserved> {
                        new productStockReserved {
                            id = product.Key,
                            type = "skuId"
                        }
                    }
                });
				
            }
            param.reserveProductStockItem = reserveStockProduct;
			param.isPartialReservationAllowed = "false";
			param.reserveProductStockState = this.reserveProductStockState;

            return GetParam(param);
        }
        
        public virtual ReserveStockRequest GetParam(ReserveStockRequest Param)
        {
            if(Param == null) {
                throw new Exception("Param cannot be null or empty");
            }

            if(Param.reservationId == null) {
                throw new Exception("Reservation Id cannot be null or empty");
            }

            if(string.IsNullOrEmpty(Param.place.id)) {
                throw new Exception("Store Id cannot be null or empty");
            }

            if (Param.reserveProductStockItem == null || (Param.reserveProductStockItem != null && Param.reserveProductStockItem.Count == 0 )) {
                throw new Exception("Request item detail cannot be null or empty");
            } 

            if (string.IsNullOrEmpty(Param.isPartialReservationAllowed)) {
                throw new Exception("Partial Reservation cannot be null or empty");
            }

            if (string.IsNullOrEmpty(Param.reserveProductStockState)) {
                throw new Exception("Product Stock State cannot be null or empty");
            }

            return Param;
        }
		
		public virtual ReserveStockRequest GetParam(Guid RecordId)
		{
            var param = BuildRequestList(new List<Guid>() {RecordId});
			return param.FirstOrDefault();
        }

        public virtual List<ReserveStockRequest> GetParamList(List<Guid> RecordIds)
        {
            return BuildRequestList(RecordIds);
        }

        public virtual string GenerateReferenceId()
        {
            try {
                long ticks = DateTime.Now.Ticks;
                string base36Ticks = Base36Encode(ticks);
                string base36Random = GenerateBase36Random(5);

                return "NCCF" + base36Ticks + base36Random;
            }
            catch(Exception e) {
                throw;
            }
        }
		
		protected virtual List<ReserveStockRequest> BuildRequestList(List<Guid> RecordIds)
        {
            var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgFeeDetail");
            var columns = new Dictionary<string, EntitySchemaQueryColumn>();

            columns.Add("StoreId", esq.AddColumn("DgLineDetail.Dg3PLService.DgStoreID"));
            columns.Add("Device", esq.AddColumn("DgResModeID"));

            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Greater, "DgSuppOfferIndex", 0));
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgFeeName", "Handset Fee"));

            var filterLineDetail = new EntitySchemaQueryFilterCollection(esq, LogicalOperationStrict.Or);
            foreach (Guid id in RecordIds) {
                filterLineDetail.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgLineDetail.Id", id));   
            }
            esq.Filters.Add(filterLineDetail);

            var entities = esq.GetEntityCollection(UserConnection);
			
			var deviceList = new List<Dictionary<string, string>>();
            foreach (var entity in entities) {
                string storeId = entity.GetTypedColumnValue<string>(columns["StoreId"].Name);
                string device = entity.GetTypedColumnValue<string>(columns["Device"].Name);
				
				var data = new Dictionary<string, string>();
				data.Add(storeId, device);
				deviceList.Add(data);
            }
			
			if(deviceList.Count == 0) {
				throw new Exception("No data can be processed");
			}
			
			var mappedData = deviceList
				.SelectMany(dict => dict)
				.GroupBy(entry => entry.Key)
				.ToDictionary(
					group => group.Key,
					group => group.Select(entry => entry.Value).ToList()
				);
			
			return mappedData.Select(item => GetParam(item.Key, item.Value)).ToList();
        }

        private string Base36Encode(long input)
        {
            const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

            string result = string.Empty;
            while(input > 0)
            {
                result = chars[(int)(input % 36)] + result;
                input /= 36;
            }

            return result;
        }

        private string GenerateBase36Random(int length)
        {
            const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            byte[] randomBytes = new byte[length];
            RNGCryptoServiceProvider RngCsp = new RNGCryptoServiceProvider();
            RngCsp.GetBytes(randomBytes);

            char[] result = new char[length];
            for(int i = 0; i < length; i++)
            {
                result[i] = chars[randomBytes[i] % 36];
            }

            return new string(result);
        }
    }
}