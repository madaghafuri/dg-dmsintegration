using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.ServiceModel.Activation;
using System.Threading.Tasks;
using System.Globalization;
using System.Net;
using System.Net.Http;
using Terrasoft.Configuration;
using Terrasoft.Core;
using Terrasoft.Core.DB;
using Terrasoft.Core.Process;
using Terrasoft.Core.Entities;
using Terrasoft.Common;
using Terrasoft.Web.Common;
using Terrasoft.Web.Http.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DgBaseService;
using DgSubmission.DgHistorySubmissionService;
using ISAIntegrationSetup;
using LookupConst = DgMasterData.DgLookupConst;
using SysSettings = Terrasoft.Core.Configuration.SysSettings;
using SolarisCore;
using System.Web;

namespace DgIntegration.DgDMS
{
    public class DMSService: BaseHttpRequest
    {
        public DMSService(UserConnection UserConnection) : base(UserConnection, "DTE", true) {
            this.UserConnection = UserConnection;
        }
        public DMSService(UserConnection UserConnection, string Type) : base(UserConnection, "DTE", Type)
        {
            this.UserConnection = UserConnection;
        }
		
		#region Token
		
		public async Task<string> GetToken(string ClientId, string ClientSecret, string GrantType)
        {
            if(string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret) || string.IsNullOrEmpty(GrantType)) {
                throw new ArgumentException("ClientId, ClientSecret, and GrantType are required");
            }

            var tokenHelper = new Token(UserConnection);
            var data = tokenHelper.GetParam(ClientId, ClientSecret, GrantType);

            return await GetToken(data, tokenHelper);
        }

		public async Task<string> GetToken()
        {
            Setup setup = this.setups
                .Where(item => item.Name == "Token")
                .FirstOrDefault();

            if(setup == null) {
                throw new InvalidOperationException("Token setup configuration not found");
            }

            var customAuth = setup?.Authentication.Custom;
            string ClientId = customAuth?.FirstOrDefault(item => item.Key == "client_id")?.Value;
            string ClientSecret = customAuth?.FirstOrDefault(item => item.Key == "client_secret")?.Value;
            string GrantType = customAuth?.FirstOrDefault(item => item.Key == "grant_type")?.Value;

            if(string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret) || string.IsNullOrEmpty(GrantType)) {
                throw new InvalidOperationException("Token authentication parameters not configured properly");
            }

            var tokenHelper = new Token(UserConnection);
            var data = tokenHelper.GetParam(ClientId, ClientSecret, GrantType);

            return await GetToken(data, tokenHelper);
        }

        public async Task<string> GetToken(TokenRequest Param)
        {
            var tokenHelper = new Token(UserConnection);
            return await GetToken(Param, tokenHelper);
        }

        private async Task<string> GetToken(TokenRequest Param, Token tokenHelper)
        {
            var tokenCache = tokenHelper.GetCacheToken();
            if(!string.IsNullOrEmpty(tokenCache)) {
                return tokenCache;
            }

            var logInfo = new LogInfo() {
                LogName = "DTE: Get Token",
                Section = "DTE (DMS)"
            };

            Setup setup = this.setups
                .FirstOrDefault(item => item.Name == "Token");

            if(setup == null || string.IsNullOrEmpty(setup.EndpointUrl)) {
                throw new InvalidOperationException("Token endpoint URL not configured");
            }

            string endpoint = setup.EndpointUrl;
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new FormUrlEncodedContent(Param.ToDictionary());

            var response = await SendRequest<TokenResponse>(request, logInfo, new TokenResponseConverter());

            if (!response.IsSuccess && response.StatusCode >= 400 && response.StatusCode <= 500 && response.Body != null) {
                var errorRes = response.Body as TokenErrorResponse;
                if(errorRes != null) {
                    string errorResponse = $"{errorRes.error}. {errorRes.error_description}";
                    throw new Exception($"Token Error: {errorResponse}");
                }
            }

            if(!response.IsSuccess) {
                throw new Exception($"Token request failed: {response.Message}");
            }

            var successRes = response.Body as TokenSuccessResponse;
            if(successRes == null || string.IsNullOrEmpty(successRes.access_token)) {
                throw new Exception("Invalid token response received");
            }

            long expiresIn = 0;
            if(successRes.expires_in != null && long.TryParse(successRes.expires_in.ToString(), out expiresIn)) {
                tokenHelper.UpdateCacheToken(successRes.access_token, expiresIn);
            } else {
                tokenHelper.UpdateCacheToken(successRes.access_token, 3600);
            }

            return successRes.access_token;
        }

		#endregion

        #region CheckStock

        public async Task<ICheckStockResponse> CheckStock(Guid lineDetailId)
        {
            var checkStock = new CheckStock(UserConnection);
			var param = checkStock.GetParam(lineDetailId);
			
            return await CheckStock(param, "DgLineDetail", lineDetailId);
        }
		
		public async Task<ICheckStockResponse> CheckStock(CheckStockRequest Param)
        {
			var checkStock = new CheckStock(UserConnection);
			var param = checkStock.GetParam(Param);
			
            return await CheckStock(param, string.Empty, Guid.Empty);
        }
		
		public async Task<ICheckStockResponse> CheckStock(string StoreId, List<string> DeviceItems)
		{
			var checkStock = new CheckStock(UserConnection);
			var param = checkStock.GetParam(StoreId, DeviceItems);
			
			return await CheckStock(param, string.Empty, Guid.Empty);
		}
		
		public async Task<List<ICheckStockResponse>> CheckStockBatch(List<Guid> lineDetailIds)
		{
			var checkStock = new CheckStock(UserConnection);
			var paramList = checkStock.GetParamList(lineDetailIds);
			
			var tasks = new List<Task<ICheckStockResponse>>();
			foreach (var param in paramList) {
				tasks.Add(CheckStock(param, string.Empty, Guid.Empty));
			}
			
			Task tasksResult = null;
			try {
				tasksResult = Task.WhenAll(tasks);
				await tasksResult;
			} catch(Exception e) {}
			
			var result = new List<ICheckStockResponse>();
			for(int i=0; i<tasks.Count; i++) {
				var task = tasks[i];
				if(task.Status != TaskStatus.RanToCompletion) {
					var exception = task.Exception;
					var innerException = exception?.InnerExceptions;
					string errorMessage = innerException?.FirstOrDefault()?.Message ?? string.Empty;

					if(!string.IsNullOrEmpty(errorMessage)) {
						result.Add(null);
					}
				}
				
				result.Add(task.Result);
			}
			
			return result;
		}

        public async Task<ICheckStockResponse> CheckStock(CheckStockRequest Param, string Section = "", Guid RecordId = default(Guid))
        {
			var logInfo = new LogInfo() {
                LogName = "DMS: Check Stock",
                Section = "DMS"
            };
			
			if(!string.IsNullOrEmpty(Section)) {
				logInfo.Section += $" ({Section})";
			}
			
			if(RecordId != Guid.Empty) {
                logInfo.RecordId = RecordId.ToString();
            }
			
			string endpoint = this.setups
				.Where(item => item.Name == "CheckStock")
				.FirstOrDefault()?
				.EndpointUrl ?? string.Empty;
				
            bool isAuth = false;
            int incrementAuth = 0;

            var res = new Response<ICheckStockResponse>();
            while (incrementAuth <= 2) {
                var request = await DefaultRequest(HttpMethod.Post, endpoint);
                request.Content = ConvertToStringContent(Param, SolarRest.JSON);

                var response = await SendRequest<ICheckStockResponse>(request, logInfo, new CheckStockResponseConverter());
                if(response.StatusCode == 401) {
                    incrementAuth++;
                    continue;
                }

                res = response;
				isAuth = true;

                break;
            }
			
			if(!isAuth) {
				throw new Exception("Please try again, token is invalid");
			}
			
			if (res.Body is CheckStockErrorResponse resError) {
                throw new Exception($"Check Stock: {resError.message}. {resError.reason}");
			}

            if (!res.IsSuccess && !string.IsNullOrEmpty(res.Message)) {
                throw new Exception(res.Message);
            }
            return res.Body as CheckStockSuccessResponse;
        }

        #endregion
		
		#region ReserveStock
		
		public async Task<IReserveStockResponse> ReserveStock(Guid LineDetailId) {
            var reserveStock = new ReserveStock(UserConnection);
            var param = reserveStock.GetParam(LineDetailId);

            return await ReserveStock(param, "DgLineDetail", LineDetailId);
        }

        public async Task<IReserveStockResponse> ReserveStock(string StoreId, List<string> DeviceItems) {
            var reserveStock = new ReserveStock(UserConnection);
            var param = reserveStock.GetParam(StoreId, DeviceItems);

            return await ReserveStock(param, string.Empty, Guid.Empty);
        }

        public async Task<List<IReserveStockResponse>> ReserveStockBatch(List<Guid> LineDetailIds)
        {
            var reserveStock = new ReserveStock(UserConnection);
            var paramList = reserveStock.GetParamList(LineDetailIds);

            var tasks = new List<Task<IReserveStockResponse>>();
            foreach (var param in paramList) {
                tasks.Add(ReserveStock(param, string.Empty, Guid.Empty));
            }

            try {
                var taskResult = Task.WhenAll(tasks);
                await taskResult;
            } catch (Exception e) {}

            var result = new List<IReserveStockResponse>();
            for (int i = 0; i < tasks.Count; i++) {
                var task = tasks[i];
                if (task.Status != TaskStatus.RanToCompletion) {
                    var exception = task.Exception;
                    var innerException = exception?.InnerExceptions;
                    string errMessage = innerException?.FirstOrDefault()?.Message ?? string.Empty;

                    if (!string.IsNullOrEmpty(errMessage)) {
                        result.Add(null);
                    }
                }
                result.Add(task.Result);
            }

            return result;
        }

        public async Task<IReserveStockResponse> ReserveStock(ReserveStockRequest Param)
        {
            var reserveStock = new ReserveStock(UserConnection);
            var param = reserveStock.GetParam(Param);

            return await ReserveStock(param, string.Empty, Guid.Empty);
        }

        public async Task<IReserveStockResponse> ReserveStock(ReserveStockRequest Param, string Section = "", Guid RecordId = default(Guid))
        {
            var logInfo = new LogInfo() {
                LogName = "DMS: Reserve Stock",
                Section = "DMS"
            };
			
			if(string.IsNullOrEmpty(Section)) {
				logInfo.Section += $" ({Section})";
			}
			
			if(RecordId != Guid.Empty) {
                logInfo.RecordId = RecordId.ToString();
            }

            string endpoint = this.setups
				.Where(item => item.Name == "ReserveUnreserve")
				.FirstOrDefault()?
				.EndpointUrl ?? string.Empty;

            int incrementAuth = 0;
            bool isAuth = false;

            var res = new Response<IReserveStockResponse>();
            while (incrementAuth <= 2) {
                var request = await DefaultRequest(HttpMethod.Post, endpoint);
                request.Content = ConvertToStringContent(Param, SolarRest.JSON);

                var response = await SendRequest<IReserveStockResponse>(request, logInfo, new ReserveStockResponseConverter());
                if (response.StatusCode == 401) {
                    incrementAuth++;
                    continue;
                }

                res = response;
                isAuth = true;

                break;
            }

            if (!isAuth) {
                throw new Exception("Please try again, token is invalid");
            }


            if (res.Body is ReserveStockErrorResponse resError) {
                throw new Exception($"Reserve Stock: {resError.message}. {resError.reason}");
            }

            if (!res.IsSuccess) {
                throw new Exception(res.Message);
            }
            return res.Body as ReserveStockSuccessResponse;
        }
		
		#endregion

        #region UnreserveStock

        public async Task<IReserveStockResponse> UnreserveStock(ReserveStockRequest Param) {
            var unreserveStock = new UnreserveStock(UserConnection);
            var param = unreserveStock.GetParam(Param);

            return await UnreserveStock(param, string.Empty, Guid.Empty);
        }

        public async Task<IReserveStockResponse> UnreserveStock(Guid LineDetailId) {
            var unreserveStock = new UnreserveStock(UserConnection);
            var param = unreserveStock.GetParam(LineDetailId);

            return await UnreserveStock(param, "DgLineDetail", LineDetailId);
        }

        public async Task<IReserveStockResponse> UnreserveStock(string StoreId, List<string> DeviceItems, string ReservationId)
        {
            var unreserveStock = new UnreserveStock(UserConnection);
            var param = unreserveStock.GetParam(StoreId, DeviceItems, ReservationId);

            return await UnreserveStock(param, string.Empty, Guid.Empty);
        }

        // public async Task<List<IReserveStockResponse>> UnreserveStockBatch(List<Guid> LineDetailIds)
        // {
        //     var unreserveStock = new UnreserveStock(UserConnection);
        //     var paramList = unreserveStock.GetParamList(LineDetailIds);

        //     var taskList = new List<Task<IReserveStockResponse>>();
        //     foreach (var param in paramList) {
        //         taskList.Add(UnreserveStock(param, string.Empty, Guid.Empty));
        //     }

        //     try {
        //         var taskResult = Task.WhenAll(taskList);
        //         await taskResult;
        //     } catch (Exception e) {}

        //     var result = new List<IReserveStockResponse>();
        //     foreach (var task in taskList) {
        //         if (task.Status != TaskStatus.RanToCompletion) {
        //             var exception = task.Exception;
        //             var innerException = exception?.InnerExceptions;
        //             string errMessage = innerException?.FirstOrDefault()?.Message ?? string.Empty;

        //             if (!string.IsNullOrEmpty(errMessage)) {
        //                 result.Add(null);
        //             }
        //         }
        //         result.Add(task.Result);
        //     }
        //     return result;
        // }

        public async Task<IReserveStockResponse> UnreserveStock(ReserveStockRequest Param, string Section = "", Guid RecordId = default(Guid))
        {
            var logInfo = new LogInfo() {
                LogName = "DMS: Unreserve Stock",
                Section = "DMS"
            };
			
			if(string.IsNullOrEmpty(Section)) {
				logInfo.Section += $" ({Section})";
			}
			
			if(RecordId != Guid.Empty) {
                logInfo.RecordId = RecordId.ToString();
            }

            string endpoint = this.setups
				.Where(item => item.Name == "ReserveUnreserve")
				.FirstOrDefault()?
				.EndpointUrl ?? string.Empty;

            int incrementAuth = 0;
            bool isAuth = false;

            var res = new Response<IReserveStockResponse>();
            while (incrementAuth <= 2) {
                var request = await DefaultRequest(HttpMethod.Post, endpoint);
                request.Content = ConvertToStringContent(Param, SolarRest.JSON);

                var response = await SendRequest<IReserveStockResponse>(request, logInfo, new ReserveStockResponseConverter());
                if (response.StatusCode == 401) {
                    incrementAuth++;
                    continue;
                }

                res = response;
                isAuth = true;

                break;
            }

            if (!isAuth) {
                throw new Exception("Please try again. Token is invalid");
            }


            if (res.Body is ReserveStockErrorResponse resError) {
                throw new Exception($"Unreserve Stock: {resError.message}. {resError.reason}");
            }

            if (!res.IsSuccess) {
                throw new Exception(res.Message);
            }
            return res.Body as ReserveStockSuccessResponse;
        }

        #endregion

        #region GetDealer

        public async Task<IDealerResponse> GetDealer(string DealerId)
        {
            var logInfo = new LogInfo() {
                LogName = "DMS: Get Dealer",
                Section = "DMS"
            };

            string endpoint = this.setups
				.Where(item => item.Name == "Dealer")
				.FirstOrDefault()?
				.EndpointUrl ?? string.Empty;

            int incrementAuth = 0;
            bool isAuth = false;

            var res = new Response<IDealerResponse>();
            while (incrementAuth <= 2) {
                var request = await DefaultRequest(HttpMethod.Get, endpoint + $"/{DealerId}");

                var response = await SendRequest<IDealerResponse>(request, logInfo, new DealerResponseConverter());
                if (response.StatusCode == 401) {
                    incrementAuth++;
                    continue;
                }

                res = response;
                isAuth = true;

                break;
            }

            if (!isAuth) {
                throw new Exception("Please try again. Token is invalid");
            }

            if (res.Body is DealerErrorResponse resError) {
                return resError;
            }

            if (!res.IsSuccess && res.Body == null) {
                throw new Exception(res.Message);
            }
            return res.Body as DealerSuccessResponse;
        }

        #endregion
		
        #region CreateProduct

        public async Task<ICreateProductResponse> CreateProduct(CreateProductRequest Param)
        {
            var createProduct = new CreateProduct(UserConnection);
            var param = createProduct.GetParam(Param);

            return await CreateProduct(param, string.Empty, string.Empty);
        }
        
        public async Task<ICreateProductResponse> CreateProduct(string ReservationID, string SOID, List<DeviceItem> DeviceItems)
        {
            var createProduct = new CreateProduct(UserConnection);
            var param = createProduct.GetParam(ReservationID, SOID, DeviceItems);

            return await CreateProduct(param, string.Empty, string.Empty);
        }

        public async Task<ICreateProductResponse> CreateProduct(string SOID)
        {
            var createProduct = new CreateProduct(UserConnection);
            var withERP = Terrasoft.Core.Configuration.SysSettings.GetValue<bool>(UserConnection, "DgIs3PLWithERP", false);
            var param = withERP ? createProduct.GetParamERP(SOID) : createProduct.GetParam(SOID);

            return await CreateProduct(param, string.Empty, string.Empty);
        }

        public async Task<ICreateProductResponse> CreateProduct(CreateProductRequest Param, string Section = "", string ReservationID = default(string))
        {
            var logInfo = new LogInfo() {
                LogName = "DMS: Create Product",
                Section = "DMS"
            };
			
			if(string.IsNullOrEmpty(Section)) {
				logInfo.Section += $" ({Section})";
			}

            string endpoint = this.setups
                .Where(item => item.Name == "CreateProduct")
                .FirstOrDefault()
                .EndpointUrl ?? string.Empty;

            int incrementAuth = 0;
            bool isAuth = false;

            var res = new Response<ICreateProductResponse>();
            while (incrementAuth <= 2) {
                var request = await DefaultRequest(HttpMethod.Post, endpoint);
                request.Content = ConvertToStringContent(Param, SolarRest.JSON);

                var response = await SendRequest<ICreateProductResponse>(request, logInfo, new CreateProductResponseConverter());
                if (response.StatusCode == 401) {
                    incrementAuth++;
                    continue;
                }

                res = response;
                isAuth = true;

                break;
            }

            if (!isAuth) {
                throw new Exception("Please try again. Token is invalid");
            }


            if (res.Body is CreateProductErrorResponse resError) {
                throw new Exception($"Create Product: {resError.message}. {resError.reason}");
            }

            if (!res.IsSuccess) {
                throw new Exception(res.Message);
            }
            return res.Body as CreateProductSuccessResponse;
        }
        #endregion

		public async Task<HttpRequestMessage> DefaultRequest(HttpMethod HttpMethod, string Endpoint)
		{
            var token = await GetToken();
            var tokenHelper = new Token(UserConnection);
            string requestId = tokenHelper.RandomRequestID();
			
			var request = new HttpRequestMessage(HttpMethod, Endpoint);
			request.Headers.Add("Accept", "application/json");
			request.Headers.Add("Authorization", "Bearer "+token);
            request.Headers.Add("X-Request-ID", requestId);
            request.Headers.Add("X-Source-System-ID", "NCCF");
			
			return request;
		}
    }
}