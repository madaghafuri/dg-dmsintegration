using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DgIntegration.DgDMS;
using DgSubmission.DgHistorySubmissionService;
using Newtonsoft.Json;
using SolarisCore;
using Terrasoft.Common;
using Terrasoft.Core;
using Terrasoft.Core.DB;
using Terrasoft.Core.Entities;
using LookupConst = DgMasterData.DgLookupConst;

namespace DgIntegration.DgCancelItemDMS
{
    public class CancelItemInDMS
    {
        private UserConnection UserConnection;
        private DMSService DMSService;
        private UnreserveStock UnreserveStock;
        public CancelItemInDMS(UserConnection UserConnection)
        {
            this.UserConnection = UserConnection;
            this.DMSService = new DMSService(UserConnection);
            this.UnreserveStock = new UnreserveStock(UserConnection);
        }

        public virtual async Task<ResultStatus> Process(Guid SubmissionId)
        {
            var result = new ResultStatus();
            try
            {
                var lineDetails = GetLineDetail(SubmissionId);
                var cancelItemGrouping = lineDetails
                    .GroupBy(item => new
                    {
                        item.ReservationID,
                        item.StoreID,
                        item.DMSOrderID
                    });

                var errorList = new List<string>();
                foreach (var itemGroup in cancelItemGrouping)
                {
                    var lineCancelRegular = itemGroup.Where(item => IsCancelRegular(item)).ToList();
                    var lineCancelIMEI = itemGroup.Where(item => IsCancelWithIMEI(item)).ToList();

                    try
                    {
                        if (lineCancelRegular.Count > 0)
                        {
                            var param = UnreserveStock.GetParam(itemGroup.Key.ReservationID);
                            var response = await DMSService.UnreserveStock(param);
                        }
                        else if (lineCancelIMEI.Count > 0)
                        {
                            var param = UnreserveStock.GetParamWithIMEI(itemGroup.Key.ReservationID);
                            var response = await DMSService.UnreserveStock(param);
                        }

                        new Update(UserConnection, "DgLineDetail")
                            .Set("DgReservationID", Column.Parameter(string.Empty))
                            .Set("DgIsUERP", Column.Parameter(false))
                            .Set("DgIsMMAG", Column.Parameter(false))
                            .Set("DgIsCommon", Column.Parameter(false))
                            .Set("DgReleasedToIPL", Column.Parameter(false))
                            .Set("DgPreDeliveryDate", Column.Parameter(null, "DateTime"))
                            .Set("DgPostDeliveryDate", Column.Parameter(null, "DateTime"))
                            .Set("DgDeliveryStatusId", Column.Parameter(null, "Guid"))
                            .Set("DgDeviceIMEI", Column.Parameter(string.Empty))
                            .Set("DgSIMCardNumber", Column.Parameter(string.Empty))
                            .Where("DgReservationID").IsEqual(Column.Parameter(itemGroup.Key.ReservationID))
                            .And("DgSubmissionId").IsEqual(Column.Parameter(SubmissionId))
                            .Execute();

                        HistorySubmissionService.InsertHistory(
                            UserConnection: UserConnection,
                            SubmissionId: SubmissionId,
                            CreatedById: UserConnection.CurrentUser.ContactId,
                            OpsId: LookupConst.Ops.ADD,
                            SectionId: LookupConst.Section.RELEASED_TO_MESAD,
                            Remark: $"[Cancel Item DMS: Unreserve] Reservation ID: {itemGroup.Key.ReservationID} Store ID: {itemGroup.Key.StoreID} success"
                        );
                    }
                    catch (System.Exception e)
                    {
                        errorList.Add($"Order ID {itemGroup.Key.DMSOrderID}, StoreID {itemGroup.Key.StoreID}: " + e.Message);
                    }
                }
                var errorMessage = string.Join("", errorList.Select(item => $"<li>{item}</li>").ToList());
                result.Message = errorList.Count == 0
                    ? "Successfully Cancel Item in DMS"
                    : JsonConvert.SerializeObject(new List<string>()
                    {
                        $"Cancel Item DMS: <br><ul>{errorMessage}</ul>"
                    });

                result.Success = errorList.Count == 0;
            }
            catch (System.Exception e)
            {
                result.Success = false;
                result.Message = e.Message;
            }

            return result;
        }

        protected virtual List<LineDetailSelected> GetLineDetail(Guid SubmissionId)
        {
            var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgLineDetail");
            var columns = new Dictionary<string, EntitySchemaQueryColumn>();

            columns.Add("LineDetailId", esq.AddColumn("Id"));
            columns.Add("PreDeliveryDate", esq.AddColumn("DgPreDeliveryDate"));
            columns.Add("DeliveryStatus", esq.AddColumn("DgDeliveryStatus.DgCode"));
            columns.Add("ReservationID", esq.AddColumn("DgReservationID"));
            columns.Add("StoreID", esq.AddColumn("Dg3PLService.DgStoreID"));
            columns.Add("DMSOrderID", esq.AddColumn("DgDMSOrderID"));

            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgCancelItemIMS", true));
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgSubmission", SubmissionId));

            var filterReservation = new EntitySchemaQueryFilterCollection(esq, LogicalOperationStrict.And);
            filterReservation.Add(esq.CreateFilterWithParameters(FilterComparisonType.NotEqual, "DgReservationID", string.Empty));
            filterReservation.Add(esq.CreateFilterWithParameters(FilterComparisonType.IsNotNull, "DgReservationID"));
            esq.Filters.Add(filterReservation);

            var entities = esq.GetEntityCollection(UserConnection);
            if (entities.Count == 0)
            {
                throw new Exception("No data can be processed");
            }

            var result = new List<LineDetailSelected>();
            foreach (var entity in entities)
            {
                result.Add(new LineDetailSelected
                {
                    Id = entity.GetTypedColumnValue<Guid>(columns["LineDetailId"].Name),
                    DeliveryStatus = entity.GetTypedColumnValue<string>(columns["DeliveryStatus"].Name),
                    PreDeliveryDate = entity.GetTypedColumnValue<DateTime>(columns["PreDeliveryDate"].Name),
                    ReservationID = entity.GetTypedColumnValue<string>(columns["ReservationID"].Name),
                    StoreID = entity.GetTypedColumnValue<string>(columns["StoreID"].Name),
                    DMSOrderID = entity.GetTypedColumnValue<string>(columns["DMSOrderID"].Name)
                });
            }

            return result;
        }

        protected virtual bool IsCancelWithIMEI(LineDetailSelected item)
        {
            return item.DeliveryStatus == "01" && item.PreDeliveryDate != null;
        }

        protected virtual bool IsCancelRegular(LineDetailSelected item)
        {
            return string.IsNullOrEmpty(item.DeliveryStatus) || item.PreDeliveryDate == null;
        }
    }

    public class LineDetailSelected
    {
        public Guid Id { get; set; }
        public DateTime PreDeliveryDate { get; set; }
        public string DeliveryStatus { get; set; }
        public string ReservationID { get; set; }
        public string StoreID { get; set; }
        public string DMSOrderID { get; set; }
    }
} 