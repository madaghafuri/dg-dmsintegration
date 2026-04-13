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
    public class UnreserveStock
    {
        protected UserConnection UserConnection;
        public UnreserveStock(UserConnection UserConnection)
        {
            this.UserConnection = UserConnection;
        }

        public virtual ReserveStockRequest GetParam(string StoreId, List<string> DeviceIds, string ReservationId)
        {
            Dictionary<string, int> reservedProductStockItems = DeviceIds
                .GroupBy(id => id)
                .ToDictionary(val => val.Key, val => val.Count());

            var param = new ReserveStockRequest();
            param.reservationId = ReservationId;
            param.place = new place()
            {
                id = StoreId,
                type = "Site"
            };
            List<reserveProductStockItem> reserveStockProduct = new List<reserveProductStockItem>();
            foreach (var product in reservedProductStockItems)
            {
                reserveStockProduct.Add(new reserveProductStockItem()
                {
                    isAllocateSerials = "false",
                    quantityRequested = new quantity()
                    {
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
            param.reserveProductStockState = "Unreserve";

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

        public virtual ReserveStockRequest GetParamWithIMEI(string ReservationId)
        {
            var param = BuildRequestWithIMEI(ReservationId);
            return param;
        }

        public virtual ReserveStockRequest GetParam(string ReservationId)
        {
            var param = BuildRequestList(ReservationId);
            return param;
        }

        public virtual ReserveStockRequest GetParam(Guid RecordId)
		{
            var param = BuildRequestList(new List<Guid>() {RecordId});
			return param;
        }

        public virtual ReserveStockRequest GetParamList(List<Guid> RecordIds)
        {
            var param = BuildRequestList(RecordIds);
            return param;
        }

        protected virtual ReserveStockRequest BuildRequestList(string ReservationId)
        {
            var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgFeeDetail");
            var columns = new Dictionary<string, EntitySchemaQueryColumn>();

            columns.Add("StoreId", esq.AddColumn("DgLineDetail.Dg3PLService.DgStoreID"));
            columns.Add("Device", esq.AddColumn("DgResModeID"));
			columns.Add("ReservationId", esq.AddColumn("DgLineDetail.DgReservationID"));
            columns.Add("IMSIType", esq.AddColumn("DgLineDetail.DgOrderIMSIType.Name"));

            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Greater, "DgSuppOfferIndex", 0));
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgFeeName", "Handset Fee"));
			esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgLineDetail.DgReservationID", ReservationId));

            var entities = esq.GetEntityCollection(UserConnection);
			
			var deviceList = new List<Dictionary<string, string>>();
            foreach (var entity in entities) {
                string storeId = entity.GetTypedColumnValue<string>(columns["StoreId"].Name);
                string device = entity.GetTypedColumnValue<string>(columns["Device"].Name);
				string reservationId = entity.GetTypedColumnValue<string>(columns["ReservationId"].Name);
                string simPackage = entity.GetTypedColumnValue<string>(columns["IMSIType"].Name);
                string usi = !string.IsNullOrEmpty(simPackage) && simPackage == "3in1 USIM_Half Size" ? "USI_200018342" : "";
				
				var data = new Dictionary<string, string>();
				data.Add("ReservationId", reservationId);
				data.Add("StoreId", storeId);
				data.Add("Device", device);
                data.Add("SIM", simPackage);
				deviceList.Add(data);
            }
			
			if(deviceList.Count == 0) {
				throw new Exception("No data can be processed");
			}
            
            var mappedData = deviceList
				.GroupBy(item => new { 
					StoreId = item["StoreId"], 
					ReservationId = item["ReservationId"] 
				})
				.Select(item => new {
					StoreId = item.Key.StoreId,
					ReservationId = item.Key.ReservationId,
					Devices = item.SelectMany(d => {
                        var devices = new List<string> {
                            d["Device"],
                        };
                        if (!string.IsNullOrEmpty(d["SIM"]) && d["SIM"] == "3in1 USIM_Half Size") {
                            var sim = Terrasoft.Core.Configuration.SysSettings.GetValue<string>(UserConnection, "DgSIMPackageCode", "USI_200018342");
                            devices.Add(sim);
                        }
                        return devices;
                    }).ToList()
				});
            var firstMap = mappedData.FirstOrDefault();

            return GetParam(firstMap.StoreId, firstMap.Devices, firstMap.ReservationId);
        }

        protected virtual ReserveStockRequest BuildRequestWithIMEI(string ReservationID)
        {
            var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgFeeDetail");
            var columns = new Dictionary<string, EntitySchemaQueryColumn>();

            columns.Add("LineID", esq.AddColumn("DgLineDetail.DgLineId"));
            columns.Add("DeviceIMEI", esq.AddColumn("DgLineDetail.DgDeviceIMEI"));
            columns.Add("ReservationID", esq.AddColumn("DgLineDetail.DgReservationID"));
            columns.Add("StoreID", esq.AddColumn("DgLineDetail.Dg3PLService.DgStoreID"));
            columns.Add("SIMCardNumber", esq.AddColumn("DgLineDetail.DgSIMCardNumber"));
            columns.Add("ItemCode", esq.AddColumn("DgResModeID"));
            columns.Add("IMSIType", esq.AddColumn("DgLineDetail.DgOrderIMSIType.Name"));

            columns["LineID"].OrderByAsc();

            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgLineDetail.DgReservationID", ReservationID));
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgFeeName", "Handset Fee"));
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Greater, "DgSuppOfferIndex", 0));

            var entities = esq.GetEntityCollection(UserConnection);
            if (entities.Count == 0)
            {
                throw new Exception("No Data can be processed");
            }

            var entity = entities.FirstOrDefault();

            var result = new ReserveStockRequest();
            result.reservationId = entity.GetTypedColumnValue<string>(columns["ReservationID"].Name);
            result.place = new place
            {
                id = entity.GetTypedColumnValue<string>(columns["StoreID"].Name),
                type = "Site"
            };
            result.reserveProductStockState = "Unreserve";
            result.isPartialReservationAllowed = "false";

            var foo = new List<Dictionary<string, string>>();
            foreach (var each in entities)
            {
                foo.Add(new Dictionary<string, string>
                {
                    {"ReservationID", each.GetTypedColumnValue<string>(columns["ReservationID"].Name)},
                    {"StoreID", each.GetTypedColumnValue<string>(columns["StoreID"].Name)},
                    {"DeviceIMEI", each.GetTypedColumnValue<string>(columns["DeviceIMEI"].Name)},
                    {"SIMCardNumber", each.GetTypedColumnValue<string>(columns["SIMCardNumber"].Name)},
                    {"ItemCode", each.GetTypedColumnValue<string>(columns["ItemCode"].Name)},
                    {"IMSIType", each.GetTypedColumnValue<string>(columns["IMSIType"].Name)}
                });
            }

            var productItem = foo
                .GroupBy(item => item["ItemCode"])
                .Select(item =>
                {
                    var reserveProductStockItem = new reserveProductStockItem
                    {
                        productSerialNumbers = new List<ProductSerialNumbers>
                        {
                            new ProductSerialNumbers {
                                listOfSerialNumbers = item
                                    .Select(device => device["DeviceIMEI"])
                                    .ToList()
                            }
                        }
                    };
                    return reserveProductStockItem;
                })
                .ToList();

            var lineWithIMSI = foo.Where(item => !string.IsNullOrEmpty(item["IMSIType"]) && item["IMSIType"] == "3in1 USIM_Half Size").ToList();
            if (lineWithIMSI.Count > 0)
            {
                var reserveItem = new reserveProductStockItem
                {
                    productSerialNumbers = new List<ProductSerialNumbers>
                    {
                        new ProductSerialNumbers {
                            listOfSerialNumbers = lineWithIMSI
                                .Select(device => device["SIMCardNumber"])
                                .ToList()
                        }
                    }
                };
                
                productItem.Add(reserveItem);
            }
            result.reserveProductStockItem = productItem;

            return GetParam(result);
        }

        protected virtual ReserveStockRequest BuildRequestList(List<Guid> RecordIds)
        {
            var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgFeeDetail");
            var columns = new Dictionary<string, EntitySchemaQueryColumn>();

            columns.Add("StoreId", esq.AddColumn("DgLineDetail.Dg3PLService.DgStoreID"));
            columns.Add("Device", esq.AddColumn("DgResModeID"));
            columns.Add("ReservationId", esq.AddColumn("DgLineDetail.DgReservationID"));
            columns.Add("IMSIType", esq.AddColumn("DgLineDetail.DgOrderIMSIType.Name"));

            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Greater, "DgSuppOfferIndex", 0));
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgFeeName", "Handset Fee"));
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.NotEqual, "DgLineDetail.DgReservationID", string.Empty));

            var filterLineDetail = new EntitySchemaQueryFilterCollection(esq, LogicalOperationStrict.Or);
            foreach (Guid id in RecordIds)
            {
                filterLineDetail.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgLineDetail.Id", id));
            }
            esq.Filters.Add(filterLineDetail);

            var entities = esq.GetEntityCollection(UserConnection);

            var deviceList = new List<Dictionary<string, string>>();
            foreach (var entity in entities)
            {
                string storeId = entity.GetTypedColumnValue<string>(columns["StoreId"].Name);
                string device = entity.GetTypedColumnValue<string>(columns["Device"].Name);
                string reservationId = entity.GetTypedColumnValue<string>(columns["ReservationId"].Name);
                string simPackage = entity.GetTypedColumnValue<string>(columns["IMSIType"].Name);
                string simPackageCode = simPackage == "3in1 USIM_Half Size" ? "USI_200018342" : "";

                var data = new Dictionary<string, string>();
                data.Add("ReservationId", reservationId);
                data.Add("StoreId", storeId);
                data.Add("Device", device);
                data.Add("SIM", simPackageCode);
                deviceList.Add(data);
            }

            if (deviceList.Count == 0)
            {
                throw new Exception("No data can be processed");
            }

            var mappedData = deviceList
                .GroupBy(item => new
                {
                    StoreId = item["StoreId"],
                    ReservationId = item["ReservationId"]
                })
                .Select(item => new
                {
                    StoreId = item.Key.StoreId,
                    ReservationId = item.Key.ReservationId,
                    // Devices = item.Select(d => d["Device"]).ToList()
                    Devices = item.SelectMany(device =>
                    {
                        var devices = new List<string> {
                            device["Device"]
                        };

                        if (!string.IsNullOrEmpty(device["SIM"]) && device["SIM"] == "USI_200018342")
                        {
                            devices.Add(device["SIM"]);
                        }

                        return devices;
                    }).ToList()
                });
            var firstMap = mappedData.FirstOrDefault();

            return GetParam(firstMap.StoreId, firstMap.Devices, firstMap.ReservationId);
        }
    }
}