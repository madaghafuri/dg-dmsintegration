using System;
using System.IO;
using System.Text;
using System.Data;
using System.Data.SqlClient;
using System.Collections;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.ServiceModel.Activation;
using Terrasoft.Configuration;
using Newtonsoft.Json;
using Terrasoft.Core;
using Terrasoft.Core.DB;
using Terrasoft.Core.Process;
using Terrasoft.Core.Entities;
using Terrasoft.Common;
using Terrasoft.Web.Common;
using Terrasoft.Web.Http.Abstractions;
using System.Reflection;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Globalization;
using Newtonsoft.Json.Linq;
using DgBaseService.DgGenericResponse;
using SysSettings = Terrasoft.Core.Configuration.SysSettings;
using System.Security.Cryptography.X509Certificates;
using ISAHttpRequest.ISAIntegrationLogService;
using System.Text.RegularExpressions;
using ISAHttpRequest.ISAHttpRequest;
using ISAEntityHelper.EntityHelper;
using System.Xml.Linq;
using System.Xml;
using ISAIntegrationSetup;

namespace DgIntegration.DgDMS
{
    public class DMSGetDealerInfoService
    {
        private UserConnection userConnection;
		private UserConnection UserConnection {
			get {
				return userConnection ?? (UserConnection)HttpContext.Current.Session["UserConnection"];
			}
		}
        private DMSService dMSService;

        public DMSGetDealerInfoService(UserConnection userConnection) {
            this.userConnection = userConnection;
            this.dMSService = new DMSService(userConnection);
        }

        public string GetEmail(string DealerCode)
        {
            var result = string.Empty;
            var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgDealer");
            var columns = new Dictionary<string, EntitySchemaQueryColumn>();

            columns.Add("DealerEmail", esq.AddColumn("DgDealerEmail"));
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgDealerID", DealerCode));
            var entities = esq.GetEntityCollection(UserConnection);

            if (entities.Count == 0) {
                return result;
            }

            foreach (var entity in entities) {
                result = entity.GetTypedColumnValue<string>(columns["DealerEmail"].Name);
            }

            return result;
        }

        public async Task<string> GetDealerInfo(string DealerCode)
        {
            var email = GetEmail(DealerCode);
            if (!string.IsNullOrEmpty(email)) {
                return email;
            }

            var dealerInfo = await this.dMSService.GetDealer(DealerCode) as DealerSuccessResponse;
            var preferedEmail = dealerInfo.contact.contactMedium.Find(item => item.baseType.Contains("Email") || item.type == "EMAIL");
            var generatedEmail = preferedEmail?.emailAddress ?? string.Empty;

            if (string.IsNullOrEmpty(generatedEmail)) {
                return string.Empty;
            }

            UpdateEmailDealer(DealerCode, generatedEmail);
            return generatedEmail;
        }

        protected void UpdateEmailDealer(string DealerCode, string Email)
        {
            var update = new Update(UserConnection, "DgDealer")
                .Set("DgDealerEmail", Column.Parameter(Email))
                .Where("DgDealerID").IsEqual(Column.Parameter(DealerCode))
                .Execute();
        }
    }
}