using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.ServiceModel.Activation;
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
    public class CheckStock
    {
        protected UserConnection UserConnection;

        public CheckStock(UserConnection UserConnection)
        {
            this.UserConnection = UserConnection;
        }

        public virtual CheckStockRequest GetParam(CheckStockRequest Param)
        {
            if(Param == null) {
                throw new Exception("Param cannot be null or empty");
            }

            if(string.IsNullOrEmpty(Param.place.id)) {
                throw new Exception("Place Id cannot be null or empty");
            }

            if(string.IsNullOrEmpty(Param.place.type)) {
                throw new Exception("Place Type cannot be null or empty");
            }

            if(Param.queryProductStockItem == null || (Param.queryProductStockItem != null && Param.queryProductStockItem.Count == 0)) {
                throw new Exception("Product Stock Item cannot be null or empty");
            }

            foreach (var item in Param.queryProductStockItem) {
                if(item.productSpecification == null) {
                    throw new Exception("Product Stock Item: Product Specification cannot be null or empty");
                }

                if(string.IsNullOrEmpty(item.productSpecification.id)) {
                    throw new Exception("Product Stock Item: Product Specification Id cannot be null or empty");
                }

                if(string.IsNullOrEmpty(item.productSpecification.name)) {
                    throw new Exception("Product Stock Item: Product Specification Name cannot be null or empty");
                }
            }

            return Param;
        }

        public virtual CheckStockRequest GetParam(string StoreId, List<string> DeviceItems)
        {
            var param = new CheckStockRequest();
            param.place = new place() {
                id = StoreId,
                type = "Site"
            };
            param.queryProductStockItem = DeviceItems
				.GroupBy(item => item)
                .Select(item => new queryProductStockItem() {
                    productSpecification = new productSpecification() {
                        id = item.Key,
                        name = "skuId"
                    }
                })
				.ToList();

            return GetParam(param);
        }

        public virtual CheckStockRequest GetParam(Guid RecordId)
		{
            var param = BuildRequestList(new List<Guid>() {RecordId});
			return param.FirstOrDefault();
        }

        public virtual List<CheckStockRequest> GetParamList(List<Guid> RecordIds)
        {
            return BuildRequestList(RecordIds);
        }
		
		public static bool IsAllAvailable(List<string> DeviceItems, CheckStockSuccessResponse CheckStockResponse)
		{
			Dictionary<string, int> deviceGroup = DeviceItems
				.GroupBy(item => item)
				.ToDictionary(
					item => item.Key,
					item => item.Count()
				);
			
			foreach(var device in deviceGroup) {
				bool isAvail = CheckStockResponse.IsAvailable(device.Key, (int)device.Value);
				if(!isAvail) {
					return false;
				}
			}
			
			return true;
		}
		
        public static List<DeviceUnavailableGroup> GetUnavailableDevice(List<string> DeviceItems, CheckStockSuccessResponse Response)
        {
            Dictionary<string, int> deviceGroup = DeviceItems
                .GroupBy(item => item)
                .ToDictionary(
                    item => item.Key,
                    item => item.Count()
                );

            var list = new List<DeviceUnavailableGroup>();

            foreach (var device in deviceGroup) {
                var emptyStock = Response.queryProductStockItem.Find(item => item.quantityAvailable.amount == 0);
                var dev = Response.queryProductStockItem.Find(item => item.productSpecification.id == device.Key);
                // if (dev == null) {
                //     var deviceObj = new DeviceUnavailableGroup {
                //         Device = device.Key,
                //         Qty = 0,
                //         QtyAvailable = 0,
                //         Message = "SKU Not Found"
                //     };
                //     list.Add(deviceObj);
                // } else if (dev.quantityAvailable.amount < device.Value) {
                //     var deviceObj = new DeviceUnavailableGroup {
                //         Device = device.Key,
                //         Qty = device.Value,
                //         QtyAvailable = dev.quantityAvailable.amount,
                //         Message = "Stock Not Available"
                //     };
                //     list.Add(deviceObj);
                // }
                if (dev != null && dev.quantityAvailable.amount < device.Value) {
                    var deviceObj = new DeviceUnavailableGroup {
                        Device = device.Key,
                        Qty = device.Value,
                        QtyAvailable = dev.quantityAvailable.amount,
                        Message = "Stock Not Available"
                    };
                    list.Add(deviceObj);
                } 
            }
            
            if (list.Count == 0 && Response != null && Response.queryProductStockItem != null && Response.queryProductStockItem.Count > 0)
            {
                foreach (var device in Response.queryProductStockItem)
                {
                    if (device.quantityAvailable.amount == 0)
                    {
                        list.Add(new DeviceUnavailableGroup
                        {
                            Device = device.productSpecification.id,
                            Qty = 1,
                            QtyAvailable = 0,
                            Message = "Stock Not Available"
                        });
                    }
                }
            }

            return list;
        }

        protected virtual List<CheckStockRequest> BuildRequestList(List<Guid> RecordIds)
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
					group => group.Select(entry => entry.Value).Distinct().ToList()
				);
			
			return mappedData.Select(item => GetParam(item.Key, item.Value)).ToList();
        }
    }

    public class DeviceUnavailableGroup
    {
        public string Device { get; set;}
        public int Qty { get; set; }
        public int QtyAvailable { get; set; }
        public string Message { get; set; }
    }
}