using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DgBaseService.DgHelpers;
using DgIntegration.DgAuto3PLProcess;
using DgIntegration.DgDMS;
using DgSubmission.DgHistorySubmissionService;
using Newtonsoft.Json;
using Terrasoft.Common;
using Terrasoft.Core;
using Terrasoft.Core.DB;
using Terrasoft.Core.Entities;
using LookupConst = DgMasterData.DgLookupConst;

namespace DgIntegration.DgAuto3PLProcessDMS
{
    public class AutoCreateProductDMS : Auto3PLProcess
    {
        public AutoCreateProductDMS(UserConnection userConnection, string soId, Guid SubmissionId, string lineCommonInventory) : base(userConnection, soId, SubmissionId, lineCommonInventory)
        {
            this.log.Name = $"Auto_Create_Product_{soId}";
        }

        public async Task<bool> Run()
        {
            bool isSuccess = false;

            log.AddMessage($"Send Request to Create Product with SO Number: {soId}. Submission Id: {submissionId.ToString()}.", true);
            string commonInventoryList = string.Join(
                ". ", 
                lineCommonInventoryList
                    .Select(item => "Reservation ID: "+item["ReservationID"]+", Store ID: "+item["StoreID"])
                    .ToArray()
            );

            try {
                isSuccess = await CreateProduct();
                if (isSuccess) {
                    return true;
                }

                SendErrorMail(commonInventoryList);
            } catch (Exception e) {
                log.AddMessage($"Something wrong happen: {Environment.NewLine}{e.ToString()}", true);
            } finally {
                log.SaveToFile();
            }

            return isSuccess;
        }

        protected async Task<bool> CreateProduct()
        {
            bool isSuccess = true;
            string msg = string.Empty;

            var service = new DgDMS.DMSService(UserConnection);
            
            var dmsOrderId = string.Empty;

            try {
                var param = new DgDMS.CreateProduct(UserConnection).GetParam(soId);
                log.AddMessage($"JSON Request: {Environment.NewLine}{JsonConvert.SerializeObject(param)}", true);

                var createProduct = await service.CreateProduct(soId) as CreateProductSuccessResponse;
                dmsOrderId = createProduct.id;
                log.AddMessage($"JSON Response: {Environment.NewLine}{JsonConvert.SerializeObject(createProduct)}", true);

                msg = "success.";
                log.AddMessage($"Create Product is Success", true);
            } catch (Exception e) {
                isSuccess = false;
                msg = $"failed. {e.Message}";
                errorMessage = e.Message;

                log.AddMessage($"Create Product is Failed: {Environment.NewLine}{errorMessage}{Environment.NewLine}{e.ToString()}", true);
            } finally {
                HistorySubmissionService.InsertHistory(
                    UserConnection: UserConnection,
                    SubmissionId: submissionId,
                    CreatedById: UserConnection.CurrentUser.ContactId,
                    OpsId: LookupConst.Ops.ADD,
                    SectionId: LookupConst.Section.RELEASED_TO_MESAD,
                    Remark: $"[Create Product] SO {soId} DMS Order ID {dmsOrderId} {msg}"
                );

                UpdateLineDetail(isSuccess, dmsOrderId);
            }

            return isSuccess;
        }

        protected void UpdateLineDetail(bool isSuccess, string DMSOrderID)
        {
            var errorMessage = new List<string>();

            var lineDetails = GetLineDetails(UserConnection, soId);
            foreach (Guid id in lineDetails) {
                try {
                    var update = new Update(UserConnection, "DgLineDetail");
                    
                    if(isSuccess) {
                        update.Set("DgIsCreateDelivery", Column.Parameter(true));
                        update.Set("DgIsMMAG", Column.Parameter(true));
                        update.Set("DgDMSOrderID", Column.Parameter(DMSOrderID));
                    } else {
                        update
                            .Set("DgReleasedToIPL", Column.Parameter(false))
                            .Set("DgIsUERP", Column.Parameter(false));
                    }

                    update
                        .Where("Id").IsEqual(Column.Parameter(id))
                        .Execute();
                } catch (Exception e) {
                    errorMessage.Add($"Update Id: {id} - {e.Message}");
                }
            }

            if(errorMessage.Count > 0) {
                log.AddMessage($"Update line detail fail: {Environment.NewLine}{string.Join(Environment.NewLine, errorMessage.ToArray())}", true);
            }
        }

        protected void SendErrorMail(string commonInventoryList)
        {
            try {
                string email = Terrasoft.Core.Configuration.SysSettings.GetValue<string>(UserConnection, "DgEmailNotification_CreateDeliveryOrderFailed", string.Empty);
                string message = $"Dear User,"
                    + $"<br><br>Create Product Order for {soId} has been Failed, "
                    + $"due to the following Exception: <strong>{errorMessage}</strong>. <br><br>"
                    + $"The stock for this <strong>{soId}</strong> has been cancelled. Please find the cancellation IDs below: <br>"
                    + commonInventoryList.Replace(". ", "<br>")
                    + $"<br><br>This message is auto-generated by NCCF.";

                var param = new MailParam() {
                    Subject = "NCCF Create Product Order Failed from SAP",
                    Message = message,
                    To = email,
                    DefaultFooterMessage = true
                };

                log.AddMessage($"Send email to {email} for error notification", true);
                Mail.Send(UserConnection, "nccf2-uerp-socreation@celcomdigi.com", param);
            } catch (Exception e) {
                log.AddMessage($"Send email error: {e.ToString()}", true);
            }
        }
    }

    public class AutoUnreserveDMS : Auto3PLProcess
    {
        public AutoUnreserveDMS(UserConnection userConnection, string soId, Guid submissionId) : base(userConnection, soId, submissionId, "")
        {
            this.log.Name = $"Auto_Unreserve_{soId}";
        }

        public async Task<bool> Run()
        {
            bool isSuccess = false;

            log.AddMessage($"Send Request to Unreserve with SO Number: {soId}. Submission Id: {submissionId.ToString()}.", true);
            try {
                isSuccess = await UnreserveDMS();
            } catch (Exception e) {
                log.AddMessage($"Something wrong happen: {Environment.NewLine}{e.ToString()}", true);
            } finally {
                log.SaveToFile();
            }

            return isSuccess;
        }

        protected async Task<bool> UnreserveDMS()
        {
            var isSuccess = false;
            var service = new DMSService(UserConnection);
            var unreserve = new UnreserveStock(UserConnection);
            var errorList = new List<string>();

            var reservationID = string.Empty;
            try {
                reservationID = GetReservationId(soId);
                if (string.IsNullOrEmpty(reservationID)) {
                    errorList.Add($"Reservation ID does not exist for SO {soId}"); 
                    throw new Exception($"Reservation ID does not exist for SO {soId}");
                }

                var param = unreserve.GetParam(reservationID);
                log.AddMessage($"JSON Request: {Environment.NewLine}{JsonConvert.SerializeObject(param)}", true);

                var unreserveService = await service.UnreserveStock(param);
                isSuccess = true;
                log.AddMessage($"JSON Response: {Environment.NewLine}{JsonConvert.SerializeObject(unreserveService)}", true);

                HistorySubmissionService.InsertHistory(
                    UserConnection: UserConnection,
                    SubmissionId: submissionId,
                    CreatedById: UserConnection.CurrentUser.ContactId,
                    OpsId: LookupConst.Ops.ADD,
                    SectionId: LookupConst.Section.RELEASED_TO_MESAD,
                    Remark: $"[Unreserve] Reservation ID: {reservationID} success"
                );

                new Update(UserConnection, "DgLineDetail")
                    .Set("DgIsCommon", Column.Parameter(false))
                    .Set("DgReservationID", Column.Parameter(string.Empty))
                    .Where("DgReservationID").IsEqual(Column.Parameter(reservationID))
                .Execute();
                log.AddMessage($"Unreserve Reservation ID: {reservationID} is success", true);
            } catch (Exception e) {
                errorList.Add($"Unreserve Reservation ID: {reservationID} failed. {e.Message}");
                log.AddMessage($"Unreserve Reservation ID: {reservationID} {Environment.NewLine}{e.ToString()}", true);
            }

            if(errorList.Count > 0) {
                HistorySubmissionService.InsertHistory(
                    UserConnection: UserConnection,
                    SubmissionId: submissionId,
                    CreatedById: UserConnection.CurrentUser.ContactId,
                    OpsId: LookupConst.Ops.ADD,
                    SectionId: LookupConst.Section.RELEASED_TO_MESAD,
                    Remark: $"[Unreserve] {string.Join(Environment.NewLine, errorList)}"
                );
            }

            return isSuccess;
        }

        protected string GetReservationId(string SOID)
        {
            var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgLineDetail");
            var columns = new Dictionary<string, EntitySchemaQueryColumn>
            {
                { "ReservationID", esq.AddColumn("DgReservationID") }
            };
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgSOID", SOID));
            var entities = esq.GetEntityCollection(UserConnection);
            var entity = entities.FirstOrDefault();

            if (entity == null) {
                return null;
            }

            return entity.GetTypedColumnValue<string>(columns["ReservationID"].Name);
        }
    }
} 