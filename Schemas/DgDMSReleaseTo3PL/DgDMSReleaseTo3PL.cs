    using System;
    using System.IO;
    using System.Text;
    using System.Data;
    using System.Data.SqlClient;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.ServiceModel;
    using System.ServiceModel.Web;
    using System.ServiceModel.Activation;
    using System.Threading.Tasks;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Globalization;
    using Terrasoft.Configuration;
    using Terrasoft.Core;
    using Terrasoft.Core.DB;
    using Terrasoft.Core.Process;
    using Terrasoft.Core.Entities;
    using Terrasoft.Common;
    using Terrasoft.Web.Common;
    using Terrasoft.Web.Http.Abstractions;
    using Newtonsoft.Json.Linq;
    using Newtonsoft.Json;
    using DgBaseService.DgGenericResponse;
    using DgBaseService.DgHelpers;
    using DgSubmission.DgHistorySubmissionService;
    using DgIntegration.DgCalculateTaxFeeService;
    using DgIntegration.DgCreateCustomerSalesOrderUERPService;
    using DgIntegration.DgMMAGOrderCreateService;
    using DgIntegration.DgDMS;
    using ISAEntityHelper.EntityHelper;
    using LookupConst = DgMasterData.DgLookupConst;
    using OrderCreateRequest = DgIntegration.DgMMAGOrderCreateService.Request;
    using OrderCreateResponse = DgIntegration.DgMMAGOrderCreateService.Response;
    using SysSettings = Terrasoft.Core.Configuration.SysSettings;


    /*
            --> DMS Check Stock --> DMS Reserve --> UERP --> Callback UERP --> DMS Create Order
            --> DMS Check Stock Gagal --> END
            --> DMS Check Stock --> DMS Reserve, gagal semua --> END
            --> DMS Check Stock --> DMS Reserve, gagal sebagian --> Unreserve yg suksesnya --> END
            --> DMS Check Stock --> DMS Reserve --> UERP Gagal --> Unreserve
            --> DMS Create Order -> Unreserve
    */


    namespace DgIntegration.DgReleaseTo3PL
    {
        public class ReleaseTo3PL
        {
            private UserConnection userConnection;
            protected UserConnection UserConnection {
                get {
                    return userConnection ?? (UserConnection)HttpContext.Current.Session["UserConnection"];
                }
            }
            private Guid SubmissionId;
            private List<LineDetailSelected> lineDetailSelected = new List<LineDetailSelected>();
            private List<LineDetailSelected> lineDetailUERP = new List<LineDetailSelected>();
            private List<DMSGroup> dmsList = new List<DMSGroup>();
            private DMSService dmsService;
            private bool isDMSSuccess = true;
            private bool isUERPSuccess = true;
            private bool isCreateProductSuccess = true;
            private string errorMessage = string.Empty;
            private List<string> errorLineList = new List<string>();
            // private ERPService ERPService;
            // private List<CancelOrderGroup> cancelList = new List<CancelOrderGroup>();

            public ReleaseTo3PL(UserConnection userConnection, Guid SubmissionId)
            {
                this.userConnection = userConnection;
                this.SubmissionId = SubmissionId;
                // var roleList = GetFunctionalRoles(userConnection);
                // this.dmsService = roleList.Contains("ERP") ? new DMSService(userConnection, "SIT3") : new DMSService(userConnection);
                this.dmsService = new DMSService(userConnection);
                // this.ERPService = new ERPService(UserConnection);
            }

            public virtual async Task<GeneralResponse> Process()
            {
                try {
                    GetLineDetail();
                    var lineDetailForDefault = this.lineDetailSelected
                        .Where(item => IsUERP(item))
                        .ToList();

                    if (lineDetailForDefault.Count == 0)
                    {
                        throw new Exception("The selected line cannot be processed in DMS/UERP. Therefore the IMSI is required.");
                    }

                    var lineDetailForDMS = lineDetailForDefault
                        .Where(item => IsDMS(item))
                        .ToList();

                    // var lineDMS = lineDetailForDMS.Select(item => item.Id).ToList();
            
                    try {
                        if (lineDetailForDMS.Count > 0) {
                            await DMSProcess(lineDetailForDMS, lineDetailForDefault);
                        } else {
                            var lineWithoutIMSI = lineDetailForDefault
                                .Where(item => string.IsNullOrEmpty(item.IMSIType))
                                .Select(item => $"<li>Line No. {item.No}</li>")
                                .ToList();

                            if (lineWithoutIMSI.Count > 0) {
                                this.errorLineList = lineWithoutIMSI;
                                throw new Exception("The selected line cannot be processed in DMS. Therefore the IMSI is required.");
                            }
                        }

                        // var userRoleList = GetFunctionalRoles(UserConnection);
                        // bool isERP = SysSettings.GetValue(UserConnection, "DgIs3PLWithERP", false);
                        string soNumber = string.Empty;

                        soNumber = await SendToUERP(lineDetailForDefault.Select(item => item.Id).ToList());

                        // if (userRoleList.Contains("Admin")) {
                        //     if (isERP) {
                        //         soNumber = await SendToERP(lineDetailForDefault.Select(item => item.Id).ToList());

                        //         var lineSuccess = this.lineDetailSelected
                        //             .Where(item => !string.IsNullOrEmpty(item.SOLineId) && !string.IsNullOrEmpty(item.SOId))
                        //             .ToList();
                        //         if (lineSuccess.Count > 0) {
                        //             soNumber = await CreateProductProcess(lineSuccess);
                        //         }
                        //     } else {
                        //     }
                        // } else {
                        //     if (userRoleList.Contains("ERP")) {
                        //         soNumber = await SendToERP(lineDetailForDefault.Select(item => item.Id).ToList());

                        //         var lineSuccess = this.lineDetailSelected
                        //             .Where(item => !string.IsNullOrEmpty(item.SOLineId) && !string.IsNullOrEmpty(item.SOId))
                        //             .ToList();
                        //         if (lineSuccess.Count > 0) {
                        //             soNumber = await CreateProductProcess(lineSuccess);
                        //         }
                        //     } else {
                        //         soNumber = await SendToUERP(lineDetailForDefault.Select(item => item.Id).ToList());
                        //     }
                        // }

                        string reserveHistoryMessage = string.Join(". ", this.dmsList
                            .Select(el => {
                                var device = el.DeviceItems
                                    .GroupBy(item => item.DeviceID)
                                    .Select(item => $"Item: {item.Key} Qty: {item.Count()}")
                                    .ToArray();

                                var message = string.Join(", ", device);
                                return $"Store ID: {el.StoreID} with {message}";
                            })
                            .ToArray());

                        var result = new GeneralResponse();
                        result.Message = $"Success creating order with SO {soNumber}.";
                        result.Success = true;

                        return result;
                    } catch (Exception e) {
                        this.errorMessage = $"[3PL Process] {e.Message}";
                    }
                } catch (Exception e) {
                    this.errorMessage = $"[Line Retrieval] {e.Message}";
                }

                return GetResponse();
            }

            #region DMS

            public virtual async Task DMSProcess(List<LineDetailSelected> dmsLine, List<LineDetailSelected> lineThroughUERP)
            {
                this.dmsList = dmsLine
                    .GroupBy(item => item.StoreID)
                    .Select(item => new DMSGroup {
                        StoreID = item.Key,
                        DeviceItems = item.SelectMany(device => {
                            var deviceItemList = new List<DeviceItem>();

                            if (!string.IsNullOrEmpty(device.DeviceID)) {
                                deviceItemList.Add(new DeviceItem {
                                    DeviceID = device.DeviceID,
                                    OfferID = device.OfferID
                                });
                            }
                            
                            if (!string.IsNullOrEmpty(device.IMSIType) && device.IMSIType == "3in1 USIM_Half Size") {
                                var simPackageCode = SysSettings.GetValue<string>(UserConnection, "DgSIMPackageCode", "USI_200018342");
                                deviceItemList.Add(new DeviceItem {
                                    DeviceID = simPackageCode,
                                    OfferID = simPackageCode
                                });
                            }

                            return deviceItemList;
                        }).ToList()
                    }).ToList();

                await CheckStockProcess();
                await ReserveStockProcess(lineThroughUERP);
            }

        // Eka Add 8 Dec 2025 --> Get Device Name from DgOffering Table
            private string GetDeviceName(string deviceId, string offerId)
            {
                var select = new Select(UserConnection)
                    .Column("DgOfferDesc")
                    .From("DgOffering")
                    .Where("DgOracleItemCode").IsEqual(Column.Parameter(deviceId)) as Select;

                if (!string.IsNullOrEmpty(offerId))
                {
                    select.And("DgOfferID").IsEqual(Column.Parameter(offerId));
                }

                using (var dbExecutor = UserConnection.EnsureDBConnection())
                using (IDataReader reader = select.ExecuteReader(dbExecutor))
                {
                    if (reader.Read())
                    {
                        return reader.GetColumnValue<string>("DgOfferDesc");
                    }
                }

                return string.Empty;
            }
            // Eka Add 8 Dec 2025 --> Get Device Name from DgOffering Table


            // Eka Add 10 Dec 2025 --> Get Device Display Name from DgDeviceSelection first, fallback to DgOffering
            private string GetDeviceDisplayName(string deviceId, string offerId)
            {
                //  DgDeviceSelection lookup
                var selectDevSel = new Select(UserConnection)
                    .Top(1)
                    .Column("DgName")
                    .From("DgDeviceSelection")
                    .Where("DgItemCode").IsEqual(Column.Parameter(deviceId)) as Select;

                using (var dbExecutor = UserConnection.EnsureDBConnection())
                using (var reader = selectDevSel.ExecuteReader(dbExecutor))
                {
                    if (reader.Read())
                    {
                        return reader.GetColumnValue<string>("DgName");
                    }
                }

                //  existing offering lookup if not found in DgDeviceSelection
                return GetDeviceName(deviceId, offerId);
            }

            // Eka Add 10 Dec 2025 --> Get Device Display Name from DgDeviceSelection first, fallback to DgOfferin
        

            protected virtual async Task CheckStockProcess()
                // {
                //     var errorCheckStock = string.Empty;
                //     for (int i = 0; i < this.dmsList.Count; i++) {
                //         var item = this.dmsList[i];
                //         string StoreID = item.StoreID;
                //         List<string> DeviceIdList = item.DeviceItems.Select(device => device.DeviceID).ToList();
                //         try {
                //             CheckStockSuccessResponse checkStockResult = await this.dmsService.CheckStock(StoreID, DeviceIdList) as CheckStockSuccessResponse;
                //             var DeviceUnavailable = CheckStock.GetUnavailableDevice(DeviceIdList, checkStockResult);
                //             if (DeviceUnavailable.Count > 0) {
                //                 var messageList = DeviceUnavailable.Select(device => $"<li>{device.Message} for Device ID {device.Device}</li>").ToArray();
                //                 throw new Exception($"Check Stock: <br><ul>{string.Join("", messageList)}</ul>");
                //             }
                //         } catch (Exception e) {
                //             errorCheckStock = e.Message;
                //             this.isDMSSuccess = false;
                //         }
                //     }
                //     if(!this.isDMSSuccess) {
                //         throw new Exception(errorCheckStock);
                //     }
                // }
            
            // Eka - Disable Line Code To Remap with New Flow  - 16 Dec 2025 // 
                    // Eka 4 Dec 2025 --> adding new Logic
                    // {
                    //     var errorList = new List<string>();
                    //     var checkTasks = this.dmsList.Select(async item =>
                    //     {
                    //         string storeID = item.StoreID;
                    //         List<string> deviceList = item.DeviceItems.Select(d => d.DeviceID).ToList();
                    //         try
                    //         {
                    //             CheckStockSuccessResponse response = await this.dmsService.CheckStock(storeID, deviceList)
                    //                 as CheckStockSuccessResponse;
                    //             var unavailable = CheckStock.GetUnavailableDevice(deviceList, response);
                    //             if (unavailable.Count > 0)
                    //             {
                    //                 var groupedRequest = item.DeviceItems
                    //                 .GroupBy(d => d.DeviceID)
                    //                 .Select(g => new { DeviceID = g.Key, OfferID = g.First().OfferID })
                    //                 .ToList();

                    //                 for (int idx = 0; idx < unavailable.Count; idx++)
                    //                 {
                    //                     var dev = unavailable[idx];
                    //                     //string offerId = item.DeviceItems.FirstOrDefault(d => d.DeviceID == dev.Device)?.OfferID;
                    //                     //string deviceName = GetDeviceName(dev.Device, offerId);

                    //                     // Eka 9 Dec 2025 --> Modify to handle multiple same device with different offer id
                    //                     string offerId = groupedRequest.Count() > idx ? groupedRequest[idx].OfferID : null;
                    //                     string deviceName = GetDeviceDisplayName(dev.Device, offerId);

                    //                      // Eka 9 Dec 2025 --> Modify to handle multiple same device with different offer id


                    //                 errorList.Add($" {dev.Device} ({deviceName}) need {dev.Qty}, available {dev.QtyAvailable}");

                    //                 }
                    //                 //item.Message = "Stock Insufficient";
                    //                 this.isDMSSuccess = false;
                    //             }
                    //         }
                    //         catch (Exception ex)
                    //         {
                    //             errorList.Add($"Store {storeID}: {ex.Message}");
                    //             item.Message = ex.Message;
                    //             this.isDMSSuccess = false;
                    //         }
                    //     }).ToList();
                    //     await Task.WhenAll(checkTasks);
                    //     if (!this.isDMSSuccess)
                    //     {
                    //         string listHtml = "<ul><li>" + string.Join("</li><li>", errorList) + "</li></ul>";
                    //         throw new Exception($"Check Stock – Stock Insufficient<br>{listHtml}");
                    //     }


                    // }
                // Eka - Disable Line Code To Remap with New Flow  - 16 Dec 2025 // 

            {
                var errorList = new List<string>();

                var checkTasks = this.dmsList.Select(async storeGroup =>
                {
                    string storeID = storeGroup.StoreID;

                    //  GROUP SKU 
                    var skuQtyMap = storeGroup.DeviceItems
                        .GroupBy(d => d.DeviceID)
                        .ToDictionary(g => g.Key, g => g.Count());

                    foreach (var sku in skuQtyMap)
                    {
                        string itemCode = sku.Key;
                        int qtyNeeded = sku.Value;

                        try
                        {
                            //  1 SKU in one REQUEST 
                            var response = await this.dmsService.CheckStock(
                                storeID,
                                new List<string> { itemCode }
                            ) as CheckStockSuccessResponse;

                            if (response == null ||
                                response.queryProductStockItem == null ||
                                response.queryProductStockItem.Count == 0)
                            {
                                errorList.Add($"{itemCode} - invalid response from DMS");
                                this.isDMSSuccess = false;
                                continue;
                            }

                            int qtyAvailable =
                                response.queryProductStockItem[0]
                                    .quantityAvailable.amount;

                            if (qtyAvailable < qtyNeeded)
                            {
                                string deviceName =
                                    GetDeviceDisplayName(itemCode,
                                        storeGroup.DeviceItems
                                            .FirstOrDefault(d => d.DeviceID == itemCode)
                                            ?.OfferID);

                                errorList.Add(
                                    $"{itemCode} ({deviceName}) need {qtyNeeded}, available {qtyAvailable}"
                                );
                                this.isDMSSuccess = false;
                            }
                        }
                        catch (Exception)
                        {
                                // DMS not found      
                            string offerId = storeGroup.DeviceItems
                            .FirstOrDefault(d => d.DeviceID == itemCode)
                            ?.OfferID;

                            string deviceName = GetDeviceDisplayName(itemCode, offerId);

                            if (!string.IsNullOrEmpty(deviceName))
                            {
                                errorList.Add($"{itemCode} ({deviceName}) not found in DMS");
                            }
                            else
                            {
                                errorList.Add($"{itemCode} not found in DMS");
                            }
                            this.isDMSSuccess = false;
                        }
                    }
                });

                await Task.WhenAll(checkTasks);

                if (!this.isDMSSuccess)
                {
                    string listHtml =
                        "<ul><li>" +
                        string.Join("</li><li>", errorList) +
                        "</li></ul>";

                    throw new Exception($"Check Stock – Failed<br>{listHtml}");
                }
            }

            protected virtual async Task ReserveStockProcess(List<LineDetailSelected> lineThroughUERP)
            {
                for (int i = 0; i < this.dmsList.Count; i++) {
                    var item = this.dmsList[i];

                    string StoreID = item.StoreID;
                    List<string> DeviceIdList = item.DeviceItems.Select(device => device.DeviceID).ToList();

                    try {
                        ReserveStockSuccessResponse reserveStockResult = await this.dmsService.ReserveStock(StoreID, DeviceIdList) as ReserveStockSuccessResponse;

                        var lines = this.lineDetailSelected.FindAll(line => DeviceIdList.Contains(line.DeviceID) || DeviceIdList.Contains(line.IMSIType)).ToList();
                        foreach (var line in lines) {
                            var index = this.lineDetailSelected.FindIndex(dev => dev.Id == line.Id);
                            this.lineDetailSelected[index].ReservationID = reserveStockResult.reservationId;
                        }
                        List<Guid> lineDetailIds = lineThroughUERP
                            .Select(line => line.Id)
                            .ToList();

                        foreach (var lineDetailId in lineDetailIds) {
                            new Update(UserConnection, "DgLineDetail")
                                .Set("DgReservationID", Column.Parameter(reserveStockResult.reservationId)) 
                                .Set("DgIsCommon", Column.Parameter(true))
                                .Where("Id").IsEqual(Column.Parameter(lineDetailId))
                                .Execute();
                        }
                        this.dmsList[i].ReservationID = reserveStockResult.reservationId;
                    } catch (Exception e) {
                        this.dmsList[i].Message = e.Message;
                        this.isDMSSuccess = false;
                    }
                }

                var successCount = this.dmsList.Where(item => !string.IsNullOrEmpty(item.ReservationID)).ToList().Count;

                if (!this.isDMSSuccess)
                {
                    if (successCount > 0)
                    {
                        await UnreserveStockProcess(this.dmsList.Where(item => !string.IsNullOrEmpty(item.ReservationID)).ToList());
                    }

                    string reserveHistoryMessage = string.Join(". ", this.dmsList
                        .Where(el => string.IsNullOrEmpty(el.ReservationID))
                        .Select(el => {
                            var device = el.DeviceItems
                                .GroupBy(item => item.DeviceID)
                                .Select(item => $"Item: {item.Key} Qty: {item.Count()}")
                                .ToArray();

                            var message = string.Join(", ", device);
                            return $"Store ID: {el.StoreID} with {message}";
                        })
                        .ToArray());
                    HistorySubmissionService.InsertHistory(
                        UserConnection: UserConnection,
                        SubmissionId: this.SubmissionId,
                        CreatedById: UserConnection.CurrentUser.ContactId,
                        OpsId: LookupConst.Ops.ADD,
                        SectionId: LookupConst.Section.RELEASED_TO_MESAD,
                        Remark: $"[Reserve] {reserveHistoryMessage}"
                    );
                    throw new Exception("Reserve Stock Process Failed.");
                }
                else
                {
                    string reserveHistoryMessage = string.Join(". ", this.dmsList
                        .Select(el => {
                            var device = el.DeviceItems
                                .GroupBy(item => item.DeviceID)
                                .Select(item => $"Item: {item.Key} Qty: {item.Count()}")
                                .ToArray();

                            var message = string.Join(", ", device);
                            return $"Store ID: {el.StoreID} with {message}";
                        })
                        .ToArray());
                    HistorySubmissionService.InsertHistory(
                        UserConnection: UserConnection,
                        SubmissionId: this.SubmissionId,
                        CreatedById: UserConnection.CurrentUser.ContactId,
                        OpsId: LookupConst.Ops.ADD,
                        SectionId: LookupConst.Section.RELEASED_TO_MESAD,
                        Remark: $"[Reserve] Success. {reserveHistoryMessage}"
                    );
                }
            }

            protected virtual async Task UnreserveStockProcess(List<DMSGroup> DMSItems = null)
            {
                if(DMSItems == null) {
                    DMSItems = this.dmsList;
                }

                for (int i = 0; i < DMSItems.Count; i++) {
                    var item = DMSItems[i];

                    string StoreID = item.StoreID;
                    List<string> DeviceIdList = item.DeviceItems.Select(device => device.DeviceID).ToList();
                    string ReservationID = item.ReservationID;

                    try {
                        var unreserveStockResult = await this.dmsService.UnreserveStock(StoreID, DeviceIdList, ReservationID);
                        List<Guid> lineDetailIds = this.lineDetailSelected
                            .Where(line => DeviceIdList.Contains(line.DeviceID))
                            .Select(line => line.Id)
                            .ToList();

                        var index = this.lineDetailSelected
                            .FindIndex(line => DeviceIdList.Contains(line.DeviceID));
                        this.lineDetailSelected[index].ReservationID = string.Empty;

                        foreach (var lineDetailId in lineDetailIds) {
                            new Update(UserConnection, "DgLineDetail")
                                .Set("DgReservationID", Column.Parameter(string.Empty))
                                .Set("DgIsCommon", Column.Parameter(false))
                                .Where("Id").IsEqual(Column.Parameter(lineDetailId))
                                .Execute();
                        }
                    } catch (Exception e) {
                        this.dmsList[i].Message = e.Message;
                        this.isDMSSuccess = false;
                    }
                }

                if (!this.isDMSSuccess) {
                    throw new Exception($"Unreserve Stock process failed.");
                }
            }

            protected virtual async Task<string> CreateProductProcess(List<LineDetailSelected> lineDetailList)
            {
                var dmsOrderId = string.Empty;
                List<DMSGroup> grouped = lineDetailList
                    .GroupBy(item => new {
                        item.ReservationID,
                        item.SONumber
                    })
                    .Select(item => new DMSGroup() {
                        DeviceItems = item.Select(device => new DeviceItem {
                            DeviceID = device.DeviceID,
                            OfferID = device.OfferID
                        }).ToList(),
                        ReservationID = item.Key.ReservationID,
                        SOID = item.Key.SONumber,

                    })
                    .ToList();
                this.dmsList = grouped;
                var log = new CustomLog(UserConnection, string.Empty);

                for (int i = 0; i < grouped.Count; i++) {
                    string reservationId = grouped[i].ReservationID;
                    string soId = grouped[i].SOID;
                    List<DeviceItem> deviceList = grouped[i].DeviceItems;
                    log.Name = $"Create_Product_{soId}";

                    try {
                        log.AddMessage($"Send Request to Create Product with SO Number: {soId}. Submission Id: {SubmissionId.ToString()}.", true);
                        var createProduct = new CreateProduct(UserConnection);
                        var rawParam = createProduct.GetParamERP(soId);
                        // Include only device
                        rawParam.productOrderItem = rawParam.productOrderItem.Where(item =>
                        {
                            var DeviceList = deviceList.Select(device => device.DeviceID).ToList();
                            var predicate = DeviceList.Contains(item.productOrderLineItem[0].product.id);
                            return predicate;
                        }).ToList();

                        var response = await this.dmsService.CreateProduct(rawParam) as CreateProductSuccessResponse;
                        dmsOrderId = response.id;
                        log.AddMessage($"JSON Response: {Environment.NewLine}{JsonConvert.SerializeObject(response)}", true);

                        new Update(UserConnection, "DgLineDetail")
                            .Set("DgIsCreateDelivery", Column.Parameter(true))
                            .Set("DgIsMMAG", Column.Parameter(true))
                            .Set("DgDMSOrderID", Column.Parameter(response.id))
                            // .Set("DgSOID", Column.Parameter(response.id))
                            .Where("DgSOID").IsEqual(Column.Parameter(soId))
                            .Execute();

                        // Insert History
                        HistorySubmissionService.Release3PL(
                            UserConnection: UserConnection,
                            SubmissionId: this.SubmissionId,
                            OFSDoNoId: soId,
                            CreatedById: UserConnection.CurrentUser.ContactId
                        );
                    } catch (Exception e) {
                        // Insert History
                        HistorySubmissionService.InsertHistory(
                            UserConnection: UserConnection,
                            SubmissionId: this.SubmissionId,
                            CreatedById: UserConnection.CurrentUser.ContactId,
                            OpsId: LookupConst.Ops.ADD,
                            SectionId: LookupConst.Section.RELEASED_TO_MESAD,
                            Remark: $"[3PL] SO {soId} failed. {e.Message}"
                        );

                        new Update(UserConnection, "DgLineDetail")
                            .Set("DgIsUERP", Column.Parameter(false))
                            .Set("DgReleasedToIPL", Column.Parameter(false))
                            .Where("DgSOID").IsEqual(Column.Parameter(soId))
                            .Execute();

                        this.dmsList[i].Message = e.Message;
                        this.isCreateProductSuccess = false;
                        SendErrorEmail(soId, reservationId, "Create Product Order", log);
                        log.AddMessage($"Create Product is Failed: {Environment.NewLine}{e.Message}{Environment.NewLine}{e.ToString()}", true);
                    } finally {
                        log.SaveToFile();
                    }
                }

                var unreserveGroup = lineDetailList
                    .GroupBy(item => new {
                        item.StoreID,
                        item.ReservationID
                    })
                    .Select(item => new DMSGroup {
                        StoreID = item.Key.StoreID,
                        DeviceItems = item.Select(device => new DeviceItem {
                            DeviceID = device.DeviceID,
                            OfferID = device.OfferID
                        }).ToList(),
                        ReservationID = item.Key.ReservationID
                    })
                    .ToList();
                
                // var cancelOrderGroup = lineDetailList
                //     .GroupBy(item => item.SOId)
                //     .Select(item => new CancelOrderGroup {
                //         SalesOrderId = item.Key,
                //         CancelOrderList = item.Select(order => new Dictionary<string, string> {
                //             {"id", order.SOLineId},
                //             {"cancellationReason", "00"}
                //         })
                //         .ToList()
                //     })
                //     .ToList();
                // this.cancelList = cancelOrderGroup;

                if (!this.isCreateProductSuccess) {
                    // await CancelOrderProcess(cancelOrderGroup);
                    await UnreserveStockProcess(unreserveGroup);
                    throw new Exception("Create Product Process Failed.");
                }

                return dmsOrderId;
            }

            #endregion

            #region UERP

            // protected virtual async Task CancelOrderProcess(List<CancelOrderGroup> GroupList)
            // {
            //     var cancel = new CancelOrder(UserConnection);
            //     for (int i = 0; i < GroupList.Count; i++) {
            //         var orderItem = GroupList[i];
            //         var cancelOrderList = orderItem.CancelOrderList
            //             .Select(item => new CancelOrderItem {
            //                 id = item["id"],
            //                 cancellationReason = item["cancellationReason"]
            //             })
            //             .ToList();
            //         var param = new ERPCancelOrderRequest {
            //             salesOrderItem = cancelOrderList
            //         };

            //         try
            //         {
            //             var result = await ERPService.CancelOrder(param, orderItem.SalesOrderId);

            //             new Update(UserConnection, "DgLineDetail")
            //                 .Set("DgSalesOrderId", Column.Parameter(string.Empty))
            //                 .Set("DgSalesOrderLineId", Column.Parameter(string.Empty))
            //                 .Set("DgSOID", Column.Parameter(string.Empty))
            //                 .Where("DgSalesOrderId").IsEqual(Column.Parameter(orderItem.SalesOrderId))
            //                 .Execute();
            //         }
            //         catch (System.Exception e)
            //         {
            //             var index = this.cancelList.FindIndex(item => orderItem.SalesOrderId == item.SalesOrderId);
            //             if (index != -1 && this.lineDetailSelected[index] != null) {
            //                 this.cancelList[index].Message = e.Message;
            //             }
            //         }
            //     }
            // }

            protected virtual async Task CalculateTaxFee(List<Guid> LineDetails)
            {
                try {
                    var service = new CalculateTaxFeeService(UserConnection);
                    await service
                        .SetParam(LineDetails)
                        .Request();
                    
                    var errorReq = service.GetBatchError();
                    if(errorReq.Count > 0) {
                        throw new Exception(string.Join("\n", errorReq));
                    }
                    
                    var response = service.GetBatchResponse();
                    for (int i = 0; i < service.LineDetailList.Count; i++) {
                        try {
                            var resItem = response[0];
                            var result = resItem?.Body?.calculateTaxFeeResponse?.ResultOfOperationReply;
                            var success = result?.resultMessage == "success" ? true : false;
                            if(!success) {
                                throw new Exception(result?.resultMessage);
                            }

                            var reply = resItem?.Body?.calculateTaxFeeResponse?.CalculateTaxFeeReply;
                            var feeAmountCalculated = reply?.feeAmtCalculated;
                            var feeItemCode = reply?.feeItemCode;

                            new Update(UserConnection, "DgFeeDetail")
                                .Set("DgFeeAmount", Column.Parameter(Convert.ToDecimal(feeAmountCalculated)))
                                .Where("DgLineDetailId").IsEqual(Column.Parameter(service.LineDetailList[i]))
                                .And("DgFeeItemCode").IsEqual(Column.Parameter(feeItemCode))
                                .Execute();	
                        } catch(Exception) {
                            continue;
                        }
                    }
                } catch(Exception) {
                    
                }
            }

            // protected virtual async Task CreateCustomerProcess(List<Guid> LineIds)
            // {
            //     var log = new CustomLog(UserConnection, string.Empty);
            //     var requestBody = string.Empty;
            //     var responseBody = string.Empty;
            //     bool success = true;
            //     try
            //     {
            //         var createCustomer = new NewCustomer(UserConnection);
            //         var param = createCustomer.GetParam(LineIds);

            //         requestBody = JsonConvert.SerializeObject(param, Formatting.Indented);
            //         log.AddMessage($"JSON Request: {Environment.NewLine}{requestBody}", true);

            //         var result = await ERPService.CreateCustomer(param);
            //         responseBody = JsonConvert.SerializeObject(result, Formatting.Indented);
            //         log.AddMessage($"JSON Response: {Environment.NewLine}{responseBody}", true);

            //         foreach (var id in LineIds)
            //         {
            //             var index = this.lineDetailSelected.FindIndex(item => item.Id == id);
            //             this.lineDetailSelected[index].SOCustomerId = result.id;
            //         }

            //         new Update(UserConnection, "DgLineDetail")
            //             .Set("DgSalesOrderCustomerId", Column.Parameter(result.id))
            //             .Where("Id").In(Column.Parameters(LineIds))
            //             .Execute();

            //         new UpdateSelect(UserConnection, "DgCRMGroup", "crmGroup")
            //             .Set("DgSOCustomerId", Column.Parameter(result.id))
            //             .From("DgCRMGroup", "crmGroup")
            //             .InnerJoin("DgSubmission").As("submission").On("submission", "DgCRMGroupId").IsEqual("crmGroup", "Id")
            //             .Where("submission", "Id").IsEqual(Column.Parameter(SubmissionId))
            //             .Execute();
            //     }
            //     catch (Exception e)
            //     {
            //         success = false;
            //         var successLine = this.dmsList.Where(item => !string.IsNullOrEmpty(item.ReservationID)).ToList();
            //         log.AddMessage($"Create Customer is Failed: {Environment.NewLine}{e.Message}{Environment.NewLine}{e.ToString()}", true);
            //         await UnreserveStockProcess(successLine);
            //         throw new Exception(e.Message);
            //     }
            //     finally
            //     {
            //         ERPService.InsertLog(UserConnection, requestBody, responseBody, string.Empty, success ? "SUCCESS" : "FAIL", "CreateNewCustomerERP");
            //     }
            // }

            // protected virtual async Task<string> CreateSalesOrderProcess(List<Guid> LineIds)
            // {
            //     var salesOrderId = string.Empty;
            //     var salesOrder = new SalesOrder(UserConnection);
            //     var log = new CustomLog(UserConnection, string.Empty);
            //     bool success = true;

            //     var requestBody = string.Empty;
            //     var responseBody = string.Empty;

            //     try
            //     {
            //         var param = salesOrder.GetParam(LineIds);
            //         requestBody = JsonConvert.SerializeObject(param, Formatting.Indented);
            //         log.AddMessage($"JSON Request: {Environment.NewLine}{requestBody}", true);

            //         var result = await ERPService.SalesOrder(param);
            //         responseBody = JsonConvert.SerializeObject(result, Formatting.Indented);
            //         log.AddMessage($"JSON Response: {Environment.NewLine}{responseBody}", true);
            //         log.Name = $"Create_Sales_Order_{result.id}";

            //         foreach (var order in result.salesOrderItem)
            //         {
            //             var index = this.lineDetailSelected.FindIndex(item => item.LineId == int.Parse(order.orderLineItemNumber));
            //             if (index != -1 && this.lineDetailSelected[index] != null)
            //             {
            //                 this.lineDetailSelected[index].SOId = result.id;
            //                 this.lineDetailSelected[index].SOLineId = order.id;
            //                 this.lineDetailSelected[index].SONumber = result.id;

            //                 if (this.lineDetailSelected[index].SOItemList == null) {
            //                     this.lineDetailSelected[index].SOItemList = new List<SalesOrderItemResponse>();
            //                 }
            //                 this.lineDetailSelected[index].SOItemList.Add(order);
            //             }
            //         }
            //         salesOrderId = result.id;
            //     }
            //     catch (Exception e)
            //     {
            //         success = false;
            //         log.AddMessage($"Create Sales Order is Failed: {Environment.NewLine}{e.Message}{Environment.NewLine}{e.ToString()}", true);
            //         SendErrorEmail(salesOrderId, string.Empty, "Create Sales Order", log);

            //         var successLine = this.dmsList.Where(item => !string.IsNullOrEmpty(item.ReservationID)).ToList();
            //         await UnreserveStockProcess(successLine);

            //         throw new Exception(e.Message);
            //     }
            //     finally
            //     {
            //         log.SaveToFile();
            //         ERPService.InsertLog(
            //             userConnection: UserConnection,
            //             Request: requestBody,
            //             Response: responseBody,
            //             salesOrderId,
            //             success ? "SUCCESS" : "FAIL",
            //             "CreateSalesOrderERP"
            //         );
            //     }

            //     return salesOrderId;
            // }

            // protected virtual async Task<string> SendToERP(List<Guid> LineIds)
            // {
            //     string soNumber = string.Empty;

            //     try
            //     {
            //         await CalculateTaxFee(LineIds);

            //         var customerList = CheckCustomerExists(LineIds);
            //         if (customerList != null && customerList.Count > 0) {
            //             await CreateCustomerProcess(customerList.Select<dynamic, Guid>(item => item.LineId).ToList());
            //         }

            //         soNumber = await CreateSalesOrderProcess(LineIds);
            //         var lineSuccess = this.lineDetailSelected
            //             .Where(item => !string.IsNullOrEmpty(item.SOLineId) && !string.IsNullOrEmpty(item.SOId))
            //             .ToList();

            //         using (DBExecutor dbExecutor = UserConnection.EnsureDBConnection()) {
            //             try
            //             {
            //                 dbExecutor.StartTransaction();
            //                 foreach (var line in lineSuccess) {
            //                     new Update(UserConnection, "DgLineDetail")
            //                         .Set("DgSOID", Column.Parameter(soNumber))
            //                         .Set("DgDateTimeReleased", Column.Parameter(DateTime.UtcNow))
            //                         .Set("Dg3PLReleasedById", Column.Parameter(UserConnection.CurrentUser.ContactId))
            //                         .Set("DgIsUERP", Column.Parameter(true))
            //                         .Set("DgCancelItemIMS", Column.Parameter(false))
            //                         .Set("DgSalesOrderId", Column.Parameter(line.SOId))
            //                         .Set("DgSalesOrderLineId", Column.Parameter(line.SOLineId))
            //                         .Where("Id").IsEqual(Column.Parameter(line.Id))
            //                     .Execute();

            //                     foreach (var item in line.SOItemList) {
            //                         new Insert(UserConnection)
            //                             .Into("DgERPOrderInfo")
            //                             .Set("DgLineDetailId", Column.Parameter(line.Id))
            //                             .Set("DgERPLineID", Column.Parameter(item.id))
            //                             .Set("DgSOID", Column.Parameter(soNumber))
            //                             .Set("DgLineItemNumber", Column.Parameter(int.Parse(item.orderLineItemNumber)))
            //                             .Set("DgMaterialCode", Column.Parameter(item.product.productSpecification.id))
            //                         .Execute();
            //                     }
            //                 }
            //                 dbExecutor.CommitTransaction();
            //             }
            //             catch (System.Exception e)
            //             {
            //                 dbExecutor.RollbackTransaction();
            //                 throw new Exception(e.Message);
            //             }
            //         }

            //         var lineFail = this.lineDetailSelected.Except(lineSuccess).ToList();
            //         if (lineFail != null && lineFail.Count > 0) {
            //             new Update(UserConnection, "DgLineDetail")
            //                 .Set("DgReleasedToIPL", Column.Parameter(false))
            //                 .Where("Id").In(Column.Parameters(lineFail.Select(item => item.Id).ToList()))
            //                 .Execute();
            //         }

            //         InsertHistoryERP(
            //             UserConnection: UserConnection,
            //             SubmissionId: this.SubmissionId,
            //             SOId: soNumber,
            //             CreatedById: UserConnection.CurrentUser.ContactId
            //         );
            //     }
            //     catch (Exception e)
            //     {
            //         throw new Exception($"Send To ERP: {e.Message}");  
            //     }

            //     return soNumber;
            // }

            // protected virtual List<dynamic> CheckCustomerExists(List<Guid> LineIds)
            // {
            //     var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgLineDetail");
            //     var columns = new Dictionary<string, EntitySchemaQueryColumn>();

            //     columns.Add("LineId", esq.AddColumn("Id"));
            //     columns.Add("CustomerId", esq.AddColumn("DgSubmission.DgCRMGroup.DgSOCustomerId"));

            //     var filterLine = new EntitySchemaQueryFilterCollection(esq, LogicalOperationStrict.Or);
            //     foreach (var id in LineIds) {
            //         filterLine.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "Id", id));
            //     }
            //     esq.Filters.Add(filterLine);

            //     var entities = esq.GetEntityCollection(UserConnection);
            //     if (entities.Count == 0) {
            //         throw new Exception("Error");
            //     }

            //     var result = new List<dynamic>();
            //     foreach (var entity in entities) {
            //         result.Add(new {
            //             CustomerId = entity.GetTypedColumnValue<string>(columns["CustomerId"].Name),
            //             LineId = entity.GetTypedColumnValue<Guid>(columns["LineId"].Name)
            //         });
            //     }

            //     var resultList = result
            //         .Where(item => string.IsNullOrEmpty(item.CustomerId))
            //         .ToList();

            //     return resultList;
            // }

            protected virtual async Task<string> SendToUERP(List<Guid> LineDetails)
            {
                string soNumber = string.Empty;
                try {
                    await CalculateTaxFee(LineDetails);
                    var service = new CreateCustomerSalesOrderUERPService(UserConnection);
                    
                    await service
                        .SetParamByLineDetail(LineDetails)
                        .Request();
                    
                    CreateCustomerSalesOrderUERPService.InsertLog(UserConnection, service.GetLog(), soNumber, service.IsSuccessResponse() ? "SUCCESS" : "FAIL");
                    
                    if(!service.IsSuccessResponse()) {
                        this.isUERPSuccess = false;
                        var successLine = this.dmsList.Where(item => !string.IsNullOrEmpty(item.ReservationID)).ToList();
                        if (successLine.Count > 0) {
                            await UnreserveStockProcess(successLine);
                        }
                        throw new Exception(service.GetErrorResponse());
                    }

                    DateTime now = DateTime.UtcNow;
                    soNumber = service.GetSONumber();
                    
                    var salesOrderLines = service.GetRequest().UERPCreateCustomerSalesOrderRequest.SalesOrderLine.Select(item => item.OrigSysLineRef).ToList();
                    var lineIdSuccess = salesOrderLines
                        .Select(item => item.Substring(0, item.IndexOf('_')))
                        .GroupBy(item => item)
                        .Select(item => Convert.ToInt32(item.Key))
                        .ToList();

                    var allLineId = this.lineDetailSelected
                        .Where(item => LineDetails.Contains(item.Id))
                        .Select(item => item.LineId)
                        .ToList();
                    
                    var lineIdFail = allLineId.Except(lineIdSuccess).ToList();
                    
                    foreach (int lineId in lineIdSuccess) {
                        new Update(UserConnection, "DgLineDetail")
                            .Set("DgSOID", Column.Parameter(soNumber))
                            .Set("DgDateTimeReleased", Column.Parameter(now))
                            .Set("Dg3PLReleasedById", Column.Parameter(UserConnection.CurrentUser.ContactId))
                            .Set("DgIsUERP", Column.Parameter(true))
                            .Set("DgCancelItemIMS", Column.Parameter(false))
                            .Where("DgLineId").IsEqual(Column.Parameter(lineId))
                        .Execute();

                        var index = this.lineDetailSelected
                            .FindIndex(item => item.LineId == lineId);
                        this.lineDetailSelected[index].SONumber = soNumber;
                        this.lineDetailSelected[index].IsUERP = true;
                    }

                    foreach(int lineId in lineIdFail) {
                        new Update(UserConnection, "DgLineDetail")
                            .Set("DgReleasedToIPL", Column.Parameter(false))
                            .Where("DgLineId").IsEqual(Column.Parameter(lineId))
                        .Execute();
                    }

                    HistorySubmissionService.ReleaseUERP(
                        UserConnection: UserConnection,
                        SubmissionId: this.SubmissionId,
                        SOId: soNumber,
                        CreatedById: UserConnection.CurrentUser.ContactId
                    );
                } catch (Exception e) {
                    if (dmsList.Count > 0) {
                        await UnreserveStockProcess();
                    }
                    throw new Exception($"Send to UERP: {e.Message}");
                }

                return soNumber;
            }

            #endregion

            public virtual GeneralResponse GetResponse()
            {
                var result = new GeneralResponse();
                bool IsSuccess = this.isDMSSuccess && this.isUERPSuccess && string.IsNullOrEmpty(this.errorMessage);

                if (IsSuccess) {
                    result.Message = string.Empty;  
                    result.Success = IsSuccess;
                    return result;
                }

                if (!this.isUERPSuccess) {
                    result.Message = this.errorMessage;
                    result.Success = false;
                    return result;
                }

                // var errorCancelList = this.cancelList.Count > 0
                //     ? this.cancelList.Where(cancel => !string.IsNullOrEmpty(cancel.Message)).Select(item => $"<li>{item.Message}</li>").ToArray()
                //     : this.errorLineList.Select(item => $"<li>{item}</li>").ToArray();

                var errorList = this.dmsList.Count > 0
                    ? this.dmsList.Where(dms => !string.IsNullOrEmpty(dms.Message)).Select(item => $"<li>{item.Message}</li>").ToArray()
                    : this.errorLineList.Select(item => $"<li>{item}</li>").ToArray();

                var detailedMessage = $"<br><ul>{string.Join("", errorList)}</ul>";

                var completedMessage = $"[\"{this.errorMessage}{detailedMessage}\"]";
                result.Message = completedMessage;
                result.Success = IsSuccess;

                return result;
            }

            protected virtual void GetLineDetail()
            {
                var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgLineDetail");

                var columns = new Dictionary<string, EntitySchemaQueryColumn>();
                columns.Add("Id", esq.AddColumn("Id"));
                columns.Add("No", esq.AddColumn("DgNo"));
                columns.Add("LineId", esq.AddColumn("DgLineId"));
                columns.Add("SONumber", esq.AddColumn("DgSOID"));
                columns.Add("OFSDoNo", esq.AddColumn("DgOFSDoNo"));
                columns.Add("SODoID", esq.AddColumn("DgSODoID"));
                columns.Add("IsUERP", esq.AddColumn("DgIsUERP"));
                columns.Add("IsCallbackUERP", esq.AddColumn("DgIsCallbackUERP"));
                columns.Add("IsDMS", esq.AddColumn("DgIsMMAG"));
                columns.Add("ReservationID", esq.AddColumn("DgReservationID"));
                columns.Add("StoreID", esq.AddColumn("Dg3PLService.DgStoreID"));
                columns.Add("IMSIType", esq.AddColumn("DgOrderIMSIType.Name"));
                columns.Add("SOCustomerId", esq.AddColumn("DgSubmission.DgCRMGroup.DgSOCustomerId"));

                columns["No"].OrderByAsc(0);
                columns["LineId"].OrderByAsc(1);

                esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgReleasedToIPL", true));
                esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgIsMMAG", false));
                esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgSubmission", this.SubmissionId));

                var entities = esq.GetEntityCollection(UserConnection);
                if(entities.Count == 0) {
                    throw new Exception("No data can be processed to 3PL");
                }

                this.lineDetailSelected = new List<LineDetailSelected>();
                foreach (var entity in entities) {
                    var temp = new LineDetailSelected() {
                        Id = entity.GetTypedColumnValue<Guid>(columns["Id"].Name),
                        No = entity.GetTypedColumnValue<int>(columns["No"].Name),
                        LineId = entity.GetTypedColumnValue<int>(columns["LineId"].Name),
                        SONumber = entity.GetTypedColumnValue<string>(columns["SONumber"].Name),
                        OFSDoNo = entity.GetTypedColumnValue<string>(columns["OFSDoNo"].Name),
                        SODoID = entity.GetTypedColumnValue<string>(columns["SODoID"].Name),
                        ReservationID = entity.GetTypedColumnValue<string>(columns["ReservationID"].Name),
                        StoreID = entity.GetTypedColumnValue<string>(columns["StoreID"].Name),
                        IMSIType = entity.GetTypedColumnValue<string>(columns["IMSIType"].Name),
                        IsUERP = entity.GetTypedColumnValue<bool>(columns["IsUERP"].Name),
                        IsCallbackUERP = entity.GetTypedColumnValue<bool>(columns["IsCallbackUERP"].Name),
                        IsDMS = entity.GetTypedColumnValue<bool>(columns["IsDMS"].Name),
                        SOCustomerId = entity.GetTypedColumnValue<string>(columns["SOCustomerId"].Name),
                        SOId = string.Empty,
                        SOLineId = string.Empty
                    };
                    this.lineDetailSelected.Add(temp);
                }

                GetItemCode();
            }

            protected virtual void GetItemCode()
            {
                QueryColumnExpression[] lineDetailIds = this.lineDetailSelected
                    .Select(item => Column.Parameter(item.Id))
                    .ToArray();

                if(lineDetailIds.Length == 0) {
                    return;
                }

                var select = new Select(UserConnection)
                    .Column("DgLineDetail", "Id").As("LineDetailId")
                    .Column("DgFeeDetail", "DgResModeID").As("ItemCode")
                    .Column("DgFeeDetail", "DgOfferID").As("OfferID")
                    .From("DgFeeDetail")
                    .Join(JoinType.LeftOuter, "DgLineDetail")
                    .On("DgLineDetail", "Id").IsEqual("DgFeeDetail", "DgLineDetailId")
                    .Where("DgFeeDetail", "DgSuppOfferIndex").IsGreater(Column.Parameter(0))
                    .And("DgFeeDetail", "DgFeeName").IsEqual(Column.Parameter("Handset Fee"))
                    .And("DgLineDetail", "Id").In(lineDetailIds) as Select;

                using(DBExecutor dbExecutor = UserConnection.EnsureDBConnection()) {
                    using(IDataReader dataReader = select.ExecuteReader(dbExecutor)) {
                        while (dataReader.Read()) {
                            int index = this.lineDetailSelected.FindIndex(item => item.Id == dataReader.GetColumnValue<Guid>("LineDetailId"));
                            this.lineDetailSelected[index].DeviceID = dataReader.GetColumnValue<string>("ItemCode");
                            this.lineDetailSelected[index].OfferID = dataReader.GetColumnValue<string>("OfferID");
                        }
                    }
                }
            }

            protected virtual void InsertHistoryERP(UserConnection UserConnection, Guid SubmissionId, string SOId, Guid CreatedById = default(Guid)) {
                var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgLineDetail");
                var columns = new Dictionary<string, EntitySchemaQueryColumn> {
                    {"LineID", esq.AddColumn("DgLineId")},
                    {"MSISDN", esq.AddColumn("DgMSISDN")}
                };
                columns["LineID"].OrderByAsc(0);

                esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgSubmission.Id", SubmissionId));
                esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgSOID", SOId));

                List<string> msisdnList = new List<string>();
                foreach (var entity in esq.GetEntityCollection(UserConnection)) {
                    string msisdn = entity.GetTypedColumnValue<string>(columns["MSISDN"].Name);
                    msisdnList.Add(msisdn);
                }

                HistorySubmissionService.InsertHistory(
                    UserConnection: UserConnection,
                    SubmissionId: SubmissionId,
                    CreatedById: CreatedById,
                    OpsId: LookupConst.Ops.ADD,
                    SectionId: LookupConst.Section.RELEASED_TO_MESAD,
                    Remark: $"[ERP] {string.Join(", ", msisdnList.ToArray())} - SO {SOId} created"
                );
            }

            protected void SendErrorEmail(string SOId, string ReservationId, string Case, CustomLog log)
            {
                try
                {
                    string email = Terrasoft.Core.Configuration.SysSettings.GetValue<string>(UserConnection, "DgEmailNotification_CreateSalesOrderFailed", string.Empty);
                    string message = $"Dear User,"
                        + $"<br><br>{Case} for {SOId} has been Failed, "
                        + $"due to the following Exception: <strong>{errorMessage}</strong>. <br><br>"
                        + $"The stock for this <strong>{SOId}</strong> has been cancelled. Please find the cancellation IDs below: <br>"
                        + $"ReservationID: {ReservationId ?? string.Empty}"
                        + $"<br><br>This message is auto-generated by NCCF.";

                    var param = new MailParam()
                    {
                        Subject = $"NCCF {Case} Failed from SAP",
                        Message = message,
                        To = email,
                        DefaultFooterMessage = true
                    };

                    log.AddMessage($"Send email to {email} for error notification", true);
                    Mail.Send(UserConnection, "nccf2-uerp-socreation@celcomdigi.com", param);
                }
                catch (Exception e)
                {
                    log.AddMessage($"Send email error: {e.ToString()}", true);
                }
            }

            protected virtual bool IsDMS(LineDetailSelected LineDetail)
            {
                return !LineDetail.IsUERP
                    && !LineDetail.IsDMS
                    && !string.IsNullOrEmpty(LineDetail.StoreID)
                    && string.IsNullOrEmpty(LineDetail.ReservationID)
                    && (!string.IsNullOrEmpty(LineDetail.DeviceID) || (!string.IsNullOrEmpty(LineDetail.IMSIType) && LineDetail.IMSIType == "3in1 USIM_Half Size"));
            }

            protected virtual bool IsUERP(LineDetailSelected LineDetail)
            {
                return !LineDetail.IsUERP && !LineDetail.IsDMS;
            }

            protected virtual bool IsCreateProduct(LineDetailSelected LineDetail)
            {
                return LineDetail.IsUERP && LineDetail.IsCallbackUERP && !LineDetail.IsDMS;
            }

            protected virtual List<string> GetFunctionalRoles(UserConnection userConnection)
            {
                var roleList = new List<string>();
                var select = new Select(userConnection)
                    .Column("roleunit", "Id").As("Id")
                    .Column("roleunit", "Name").As("RoleName")
                    .From("SysAdminUnit").As("sau")
                    .Join(JoinType.LeftOuter, "SysUserInRole").As("suir")
                        .On("suir", "SysUserId").IsEqual("sau", "Id")
                    .Join(JoinType.LeftOuter, "SysAdminUnit").As("roleunit")
                        .On("suir", "SysRoleId").IsEqual("roleunit", "Id")
                    .Where("sau", "Id").IsEqual(Column.Parameter(userConnection.CurrentUser.Id)) as Select;

                using (DBExecutor dbExecutor = userConnection.EnsureDBConnection()) {
                    using (var reader = select.ExecuteReader(dbExecutor)) {
                        while (reader.Read()) {
                            string role = reader.GetColumnValue<string>("RoleName");

                            if (role == "Admin" || role == "Operation" || role == "ERP") {
                                roleList.Add(role);
                            }
                        }
                    }
                }

                return roleList;
            }
        }

        public class LineDetailSelected
        {
            public Guid Id { get; set; }
            public int No { get; set; }
            public int LineId { get; set; }
            public string SONumber { get; set; }
            public string OFSDoNo { get; set; }
            public string SODoID { get; set; }
            public bool IsUERP { get; set; }
            public bool IsCallbackUERP { get; set; }
            public bool IsDMS { get; set; }
            public string ReservationID { get; set; }
            public string StoreID { get; set; }
            public string DeviceID { get; set; }
            public string IMSIType { get; set; }
            public string OfferID { get; set; }
            public string SOLineId { get; set; }
            public string SOCustomerId { get; set; }
            public string SOId { get; set; }
            // public List<SalesOrderItemResponse> SOItemList { get; set; }
        }

        public class DMSGroup
        {
            public string StoreID { get; set; }
            public List<DeviceItem> DeviceItems { get; set; }
            public string Message { get; set; }
            public string ReservationID { get; set; }
            public string SOID { get; set; }
        }
    }