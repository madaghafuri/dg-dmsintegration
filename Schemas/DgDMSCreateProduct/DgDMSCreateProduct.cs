using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Terrasoft.Configuration;
using Terrasoft.Core;
using Terrasoft.Core.Entities;


namespace DgIntegration.DgDMS
{
    public class CreateProduct
    {
        protected UserConnection UserConnection;

        public CreateProduct(UserConnection UserConnection)
        {
            this.UserConnection = UserConnection;
        }

        public virtual CreateProductRequest GetParam(string ReservationID, string SOID, List<DeviceItem> DeviceItems)
        {
            var param = BuildRequest(ReservationID, SOID, DeviceItems);
            return param;
        }

        public virtual CreateProductRequest GetParam(string soId)
        {
            var param = BuildRequest(soId);
            return param;
        }

        public virtual CreateProductRequest GetParamERP(string soId)
        {
            var request = BuildRequestWithERPBySOID(soId);
            var param = BuildRequestWithERP(request.entities, request.columns);

            return param;
        }

        public virtual CreateProductRequest GetParam(CreateProductRequest Param)
        {
            if (string.IsNullOrEmpty(Param.completionDate))
            {
                throw new Exception("Completion Date cannot be null or empty");
            }

            if (Param.externalReference == null || (Param.externalReference != null && Param.externalReference.Count == 0))
            {
                throw new Exception("External Reference cannot be null or empty");
            }

            if (Param.productOrderItem == null || (Param.productOrderItem != null && Param.productOrderItem.Count == 0))
            {
                throw new Exception("Product Order Item cannot be null or empty");
            }

            if (Param.relatedParty == null || (Param.relatedParty != null && Param.relatedParty.Count == 0))
            {
                throw new Exception("Related Party cannot be null or empty");
            }

            if (Param.relatedParty != null && Param.relatedParty.Count > 0)
            {
                foreach (var party in Param.relatedParty)
                {
                    if (party.contactMedium != null && party.contactMedium.Count > 0)
                    {
                        foreach (var contact in party.contactMedium)
                        {
                            var valid = ValidateAddress(contact.characteristic.streetAddress1);
                            if (!valid.IsValid)
                            {
                                throw new Exception(valid.Message);
                            }
                        }
                    }
                }
            }

            return Param;
        }

        protected virtual (bool IsValid, string Message) ValidateAddress(string address)
        {
            string allowedPattern = @"[a-zA-Z0-9\s\.,\-\'\( \)#]";
            var forbiddenChars = address
                .Where(c => !Regex.IsMatch(c.ToString(), allowedPattern))
                .Distinct()
                .ToList();

            if (forbiddenChars.Any())
            {
                string illegalList = string.Join(" ", forbiddenChars);
                return (false, $"Delivery Address Validation: The following characters are not allowed: {illegalList}");
            }

            return (true, "Valid");
        }

        public virtual dynamic BuildRequestWithERPBySOID(string SOID) {
            var query = BuildQuery();
            EntitySchemaQuery esq = query.esq;
            Dictionary<string, EntitySchemaQueryColumn> columns = query.columns; 

            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgSOID", SOID));

            var entities = esq.GetEntityCollection(UserConnection);

            return new {
                entities,
                columns
            };
        }

        public virtual CreateProductRequest BuildRequestWithERP(EntityCollection entities, Dictionary<string, EntitySchemaQueryColumn> columns) {
            var entity = entities.FirstOrDefault();
            if (entity == null) {
                throw new Exception("No line detail can be processed");
            }

            var Admin2Name = entity.GetTypedColumnValue<string>(columns["Admin2Name"].Name);
            var street1 = entity.GetTypedColumnValue<string>(columns["StreetAddress1"].Name);
            var street2 = string.Empty;
            var soId = entity.GetTypedColumnValue<string>(columns["SOID"].Name);

            if (!string.IsNullOrEmpty(street1) && street1.Length > 200)
            {
                street2 = street1.Substring(200);
                street1 = street1.Substring(0, 200);
            }

            var externalReference = new List<CPExternalReference>
            {
                new CPExternalReference
                {
                    externalIdentifierType = "ChannelReference",
                    id = entity.GetTypedColumnValue<string>(columns["SOID"].Name)
                }
            };

            var relatedParty = new List<CPRelatedParty>
            {
                new CPRelatedParty {
                    name = entity.GetTypedColumnValue<string>(columns["Admin1Name"].Name),
                    id = Masking(entity.GetTypedColumnValue<string>(columns["Admin1IdNo"].Name)),
                    role = "customerdetails",
                    contactMedium = new List<contactMedium> {
                        new contactMedium {
                            mediumType = "collated",
                            characteristic = new characteristic {
                                streetAddress1 = street1,
                                streetAddress2 = street2,
                                postcode = entity.GetTypedColumnValue<string>(columns["PostCode"].Name),
                                city = entity.GetTypedColumnValue<string>(columns["City"].Name),
                                state = entity.GetTypedColumnValue<string>(columns["State"].Name),
                                country = entity.GetTypedColumnValue<string>(columns["Country"].Name),
                                emailAddress = entity.GetTypedColumnValue<string>(columns["Admin1Email"].Name),
                                faxNumber = entity.GetTypedColumnValue<string>(columns["FaxNumber"].Name),
                                phoneNumber = entity.GetTypedColumnValue<string>(columns["Admin1Phone"].Name)
                            }
                        }
                    }
                }
            };

            if (!string.IsNullOrEmpty(Admin2Name))
            {
                var newParty = new CPRelatedParty
                {
                    name = Admin2Name,
                    id = Masking(entity.GetTypedColumnValue<string>(columns["Admin2IdNo"].Name)),
                    role = "altcustomerdetls",
                    contactMedium = new List<contactMedium>
                    {
                        new contactMedium {
                            mediumType = "collated",
                            characteristic = new characteristic {
                                streetAddress1 = street1,
                                streetAddress2 = street2,
                                postcode = entity.GetTypedColumnValue<string>(columns["PostCode"].Name),
                                city = entity.GetTypedColumnValue<string>(columns["City"].Name),
                                state = entity.GetTypedColumnValue<string>(columns["State"].Name),
                                country = entity.GetTypedColumnValue<string>(columns["Country"].Name),
                                emailAddress = entity.GetTypedColumnValue<string>(columns["Admin2Email"].Name),
                                faxNumber = entity.GetTypedColumnValue<string>(columns["FaxNumber"].Name),
                                phoneNumber = entity.GetTypedColumnValue<string>(columns["Admin2Phone"].Name)
                            }
                        }
                    }
                };
                relatedParty.Add(newParty);
            }
            else
            {
                var newParty = new CPRelatedParty
                {
                    name = entity.GetTypedColumnValue<string>(columns["Auth1Name"].Name),
                    id = Masking(entity.GetTypedColumnValue<string>(columns["Auth1IdNo"].Name)),
                    role = "altcustomerdetls",
                    contactMedium = new List<contactMedium>
                    {
                        new contactMedium {
                            mediumType = "collated",
                            characteristic = new characteristic {
                                streetAddress1 = street1,
                                streetAddress2 = street2,
                                postcode = entity.GetTypedColumnValue<string>(columns["PostCode"].Name),
                                city = entity.GetTypedColumnValue<string>(columns["City"].Name),
                                state = entity.GetTypedColumnValue<string>(columns["State"].Name),
                                country = entity.GetTypedColumnValue<string>(columns["Country"].Name),
                                emailAddress = entity.GetTypedColumnValue<string>(columns["Auth1Email"].Name),
                                faxNumber = entity.GetTypedColumnValue<string>(columns["FaxNumber"].Name),
                                phoneNumber = entity.GetTypedColumnValue<string>(columns["Auth1Phone"].Name)
                            }
                        }
                    }
                };
                relatedParty.Add(newParty);
            }

            var productOrderItem = GetItemERP(soId);
            
            var request = new CreateProductRequest();
            request.externalReference = externalReference;
            request.completionDate = DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd");
            request.relatedParty = relatedParty;
            request.note = new List<Note>
                {
                    new Note {
                        id = "customerName",
                        text = entity.GetTypedColumnValue<string>(columns["CustomerName"].Name)
                    },
                    new Note {
                        id = "customerId",
                        text = entity.GetTypedColumnValue<string>(columns["CustomerBRN"].Name)
                    }
                };
            request.productOrderItem = productOrderItem;

            return GetParam(request);
        }

        public virtual CreateProductRequest BuildRequest(string ReservationID, string SOID, List<DeviceItem> DeviceItems)
        {
            var externalReference = new List<CPExternalReference>();
            externalReference.Add(new CPExternalReference()
            {
                externalIdentifierType = ReservationID,
                id = SOID
            });

            var productOrderItem = DeviceItems
                .GroupBy(item => item.DeviceID)
                .Select((item, index) => new productOrderItem()
                {
                    id = $"{index + 1}",
                    reservationId = ReservationID,
                    action = "add",
                    productOrderLineItem = new List<productOrderLineItem> {
                        new productOrderLineItem {
                            id = item.Key,
                            action = "add",
                            quantity = 1,
                            product = new product {
                                id = item.Key,
                                name = item.Key
                            }
                        }
                    },
                    quantity = item.Count()
                })
                .ToList();

            var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgLineDetail");
            var columns = new Dictionary<string, EntitySchemaQueryColumn>();

            columns.Add("IMSIType", esq.AddColumn("DgOrderIMSIType"));
            columns.Add("Username", esq.AddColumn("DgUsername"));
            columns.Add("StreetAddress1", esq.AddColumn("DgSubmission.DgCRMGroup.DgDeliveryaddress"));
            columns.Add("PostCode", esq.AddColumn("DgSubmission.DgCRMGroup.DgPostcodeAdmInformationDelivery.Name"));
            columns.Add("City", esq.AddColumn("DgSubmission.DgCRMGroup.DgCityAdmInformationDelivery.Name"));
            columns.Add("State", esq.AddColumn("DgSubmission.DgCRMGroup.DgStateAdmInfoDelivery.Name"));
            columns.Add("Country", esq.AddColumn("DgSubmission.DgCRMGroup.DgCountryAdmInformationDelivery.Name"));
            columns.Add("EmailAddress", esq.AddColumn("DgSubmission.DgCRMGroup.DgCompanyEmail"));
            columns.Add("FaxNumber", esq.AddColumn("DgSubmission.DgCRMGroup.DgFaxNo"));
            columns.Add("PhoneNumber", esq.AddColumn("DgSubmission.DgCRMGroup.DgTelNo"));
            columns.Add("CustomerName", esq.AddColumn("DgSubmission.DgCRMGroup.DgGroupName"));
            columns.Add("CustomerBRN", esq.AddColumn("DgSubmission.DgCRMGroup.DgBRN"));

            //customerdetails data
            columns.Add("Admin1Name", esq.AddColumn("DgSubmission.DgCRMGroup.DgAdministrationName1"));
            columns.Add("Admin1Email", esq.AddColumn("DgSubmission.DgCRMGroup.DgAdministrationEmail1"));
            columns.Add("Admin1Phone", esq.AddColumn("DgSubmission.DgCRMGroup.DgMobilePhone1"));
            columns.Add("Admin1IdNo", esq.AddColumn("DgSubmission.DgCRMGroup.DgIdNo1"));

            //Admin 2 data
            columns.Add("Admin2Name", esq.AddColumn("DgSubmission.DgCRMGroup.DgAdministrationName2"));
            columns.Add("Admin2Email", esq.AddColumn("DgSubmission.DgCRMGroup.DgAdministrationEmail2"));
            columns.Add("Admin2Phone", esq.AddColumn("DgSubmission.DgCRMGroup.DgMobilePhone2"));
            columns.Add("Admin2IdNo", esq.AddColumn("DgSubmission.DgCRMGroup.DgIdNo2"));

            //altcustomerdetails data
            columns.Add("Auth1Name", esq.AddColumn("DgSubmission.DgCRMGroup.DgAuthorizedName1"));
            columns.Add("Auth1Email", esq.AddColumn("DgSubmission.DgCRMGroup.DgAuthorizedEmail1"));
            columns.Add("Auth1Phone", esq.AddColumn("DgSubmission.DgCRMGroup.DgAuthorizedMobilePhone1"));
            columns.Add("Auth1IdNo", esq.AddColumn("DgSubmission.DgCRMGroup.DgAuthorizedIdNo1"));

            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgReservationID", ReservationID));

            var entities = esq.GetEntityCollection(UserConnection);
            if (entities.Count == 0)
            {
                throw new Exception($"Records with reservation id {ReservationID} does not exist");
            }
            var entity = entities.FirstOrDefault();
            var Admin2Name = entity.GetTypedColumnValue<string>(columns["Admin2Name"].Name);

            var street1 = entity.GetTypedColumnValue<string>(columns["StreetAddress1"].Name);
            var street2 = string.Empty;

            if (!string.IsNullOrEmpty(street1) && street1.Length > 200)
            {
                street2 = street1.Substring(200);
                street1 = street1.Substring(0, 200);
            }

            var queryResult = new List<CPQueryResult>();
            foreach (var entity1 in entities)
            {
                queryResult.Add(new CPQueryResult()
                {
                    Username = entity1.GetTypedColumnValue<string>(columns["Username"].Name),
                    StreetAddress1 = entity1.GetTypedColumnValue<string>(columns["StreetAddress1"].Name),
                    PostCode = entity1.GetTypedColumnValue<string>(columns["PostCode"].Name),
                    City = entity1.GetTypedColumnValue<string>(columns["City"].Name),
                    State = entity1.GetTypedColumnValue<string>(columns["State"].Name),
                    Country = entity1.GetTypedColumnValue<string>(columns["Country"].Name),
                    EmailAddress = entity1.GetTypedColumnValue<string>(columns["EmailAddress"].Name),
                    FaxNumber = entity1.GetTypedColumnValue<string>(columns["FaxNumber"].Name),
                    PhoneNumber = entity1.GetTypedColumnValue<string>(columns["PhoneNumber"].Name),
                });
            }

            var relatedParty = new List<CPRelatedParty>
            {
                new CPRelatedParty {
                    name = entity.GetTypedColumnValue<string>(columns["Admin1Name"].Name),
                    id = Masking(entity.GetTypedColumnValue<string>(columns["Admin1IdNo"].Name)),
                    role = "customerdetails",
                    contactMedium = new List<contactMedium> {
                        new contactMedium {
                            mediumType = "collated",
                            characteristic = new characteristic {
                                streetAddress1 = street1,
                                streetAddress2 = street2,
                                postcode = entity.GetTypedColumnValue<string>(columns["PostCode"].Name),
                                city = entity.GetTypedColumnValue<string>(columns["City"].Name),
                                state = entity.GetTypedColumnValue<string>(columns["State"].Name),
                                country = entity.GetTypedColumnValue<string>(columns["Country"].Name),
                                emailAddress = entity.GetTypedColumnValue<string>(columns["Admin1Email"].Name),
                                faxNumber = entity.GetTypedColumnValue<string>(columns["FaxNumber"].Name),
                                phoneNumber = entity.GetTypedColumnValue<string>(columns["Admin1Phone"].Name)
                            }
                        }
                    }
                }
            };

            if (!string.IsNullOrEmpty(Admin2Name))
            {
                var newParty = new CPRelatedParty
                {
                    name = Admin2Name,
                    id = Masking(entity.GetTypedColumnValue<string>(columns["Admin2IdNo"].Name)),
                    role = "altcustomerdetls",
                    contactMedium = new List<contactMedium>
                    {
                        new contactMedium {
                            mediumType = "collated",
                            characteristic = new characteristic {
                                streetAddress1 = street1,
                                streetAddress2 = street2,
                                postcode = entity.GetTypedColumnValue<string>(columns["PostCode"].Name),
                                city = entity.GetTypedColumnValue<string>(columns["City"].Name),
                                state = entity.GetTypedColumnValue<string>(columns["State"].Name),
                                country = entity.GetTypedColumnValue<string>(columns["Country"].Name),
                                emailAddress = entity.GetTypedColumnValue<string>(columns["Admin2Email"].Name),
                                faxNumber = entity.GetTypedColumnValue<string>(columns["FaxNumber"].Name),
                                phoneNumber = entity.GetTypedColumnValue<string>(columns["Admin2Phone"].Name)
                            }
                        }
                    }
                };
                relatedParty.Add(newParty);
            }
            else
            {
                var newParty = new CPRelatedParty
                {
                    name = entity.GetTypedColumnValue<string>(columns["Auth1Name"].Name),
                    id = Masking(entity.GetTypedColumnValue<string>(columns["Auth1IdNo"].Name)),
                    role = "altcustomerdetls",
                    contactMedium = new List<contactMedium>
                    {
                        new contactMedium {
                            mediumType = "collated",
                            characteristic = new characteristic {
                                streetAddress1 = street1,
                                streetAddress2 = street2,
                                postcode = entity.GetTypedColumnValue<string>(columns["PostCode"].Name),
                                city = entity.GetTypedColumnValue<string>(columns["City"].Name),
                                state = entity.GetTypedColumnValue<string>(columns["State"].Name),
                                country = entity.GetTypedColumnValue<string>(columns["Country"].Name),
                                emailAddress = entity.GetTypedColumnValue<string>(columns["Auth1Email"].Name),
                                faxNumber = entity.GetTypedColumnValue<string>(columns["FaxNumber"].Name),
                                phoneNumber = entity.GetTypedColumnValue<string>(columns["Auth1Phone"].Name)
                            }
                        }
                    }
                };
                relatedParty.Add(newParty);
            }

            var request = new CreateProductRequest()
            {
                externalReference = externalReference,
                completionDate = DateTime.Now.ToString("yyyy-MM-dd"),
                productOrderItem = productOrderItem,
                relatedParty = relatedParty,
                note = new List<Note>
                {
                    new Note {
                        id = "customerName",
                        text = entity.GetTypedColumnValue<string>(columns["CustomerName"].Name)
                    },
                    new Note {
                        id = "customerId",
                        text = entity.GetTypedColumnValue<string>(columns["CustomerBRN"].Name)
                    }
                }
            };

            var poIMSIItem = new List<productOrderItem>();
            var lineWithIMSI = queryResult.Where(item => !string.IsNullOrEmpty(item.IMSIType) && item.IMSIType == "3in1 USIM_Half Size").ToList();
            if (lineWithIMSI.Count > 0)
            {
                for (int i = 0; i <= lineWithIMSI.Count; i++)
                {
                    poIMSIItem.Add(new productOrderItem
                    {
                        id = (productOrderItem.Count + i + 1).ToString(),
                        reservationId = ReservationID,
                        action = "add",
                        productOrderLineItem = new List<productOrderLineItem> {
                            new productOrderLineItem {
                                id = lineWithIMSI[i].LineID,
                                action = "add",
                                quantity = 1,
                                product = new product {
                                    id = "USI_200018342",
                                    name = "USI_200018342"
                                }
                            }
                        },
                        quantity = 1
                    });
                }

                request.productOrderItem.AddRange(poIMSIItem);
            }

            return GetParam(request);
        }

        public virtual CreateProductRequest BuildRequest(string soId)
        {
            var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgLineDetail");
            var columns = new Dictionary<string, EntitySchemaQueryColumn>();

            columns.Add("Id", esq.AddColumn("Id"));
            columns.Add("ReservationID", esq.AddColumn("DgReservationID"));
            columns.Add("SOID", esq.AddColumn("DgSOID"));
            columns.Add("IMSIType", esq.AddColumn("DgOrderIMSIType.Name"));
            columns.Add("Username", esq.AddColumn("DgUsername"));
            columns.Add("LineID", esq.AddColumn("DgLineId"));

            columns.Add("StreetAddress1", esq.AddColumn("DgSubmission.DgCRMGroup.DgDeliveryaddress"));
            columns.Add("PostCode", esq.AddColumn("DgSubmission.DgCRMGroup.DgPostcodeAdmInformationDelivery.Name"));
            columns.Add("City", esq.AddColumn("DgSubmission.DgCRMGroup.DgCityAdmInformationDelivery.Name"));
            columns.Add("State", esq.AddColumn("DgSubmission.DgCRMGroup.DgStateAdmInfoDelivery.Name"));
            columns.Add("Country", esq.AddColumn("DgSubmission.DgCRMGroup.DgCountryAdmInformationDelivery.Name"));
            columns.Add("EmailAddress", esq.AddColumn("DgSubmission.DgCRMGroup.DgCompanyEmail"));
            columns.Add("FaxNumber", esq.AddColumn("DgSubmission.DgCRMGroup.DgFaxNo"));
            columns.Add("PhoneNumber", esq.AddColumn("DgSubmission.DgCRMGroup.DgTelNo"));
            columns.Add("CustomerName", esq.AddColumn("DgSubmission.DgCRMGroup.DgGroupName"));
            columns.Add("CustomerBRN", esq.AddColumn("DgSubmission.DgCRMGroup.DgBRN"));

            //customerdetails data
            columns.Add("Admin1Name", esq.AddColumn("DgSubmission.DgCRMGroup.DgAdministrationName1"));
            columns.Add("Admin1Email", esq.AddColumn("DgSubmission.DgCRMGroup.DgAdministrationEmail1"));
            columns.Add("Admin1Phone", esq.AddColumn("DgSubmission.DgCRMGroup.DgMobilePhone1"));
            columns.Add("Admin1IdNo", esq.AddColumn("DgSubmission.DgCRMGroup.DgIdNo1"));

            //Admin 2 data
            columns.Add("Admin2Name", esq.AddColumn("DgSubmission.DgCRMGroup.DgAdministrationName2"));
            columns.Add("Admin2Email", esq.AddColumn("DgSubmission.DgCRMGroup.DgAdministrationEmail2"));
            columns.Add("Admin2Phone", esq.AddColumn("DgSubmission.DgCRMGroup.DgMobilePhone2"));
            columns.Add("Admin2IdNo", esq.AddColumn("DgSubmission.DgCRMGroup.DgIdNo2"));

            //altcustomerdetails data
            columns.Add("Auth1Name", esq.AddColumn("DgSubmission.DgCRMGroup.DgAuthorizedName1"));
            columns.Add("Auth1Email", esq.AddColumn("DgSubmission.DgCRMGroup.DgAuthorizedEmail1"));
            columns.Add("Auth1Phone", esq.AddColumn("DgSubmission.DgCRMGroup.DgAuthorizedMobilePhone1"));
            columns.Add("Auth1IdNo", esq.AddColumn("DgSubmission.DgCRMGroup.DgAuthorizedIdNo1"));

            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgSOID", soId));
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgIsMMAG", false));

            columns["LineID"].OrderByAsc(0);

            var entities = esq.GetEntityCollection(UserConnection);
            if (entities.Count == 0)
            {
                throw new Exception($"Records with so id {soId} does not exist");
            }
            var entity = entities.FirstOrDefault();

            var request = new CreateProductRequest();
            request.productOrderItem = new List<productOrderItem>();

            var queryResult = new List<CPQueryResult>();
            var imsiItems = new List<productOrderItem>();
            for (int i = 0; i < entities.Count; i++)
            {
                var lineId = entities[i].GetTypedColumnValue<int>(columns["LineID"].Name).ToString();
                queryResult.Add(new CPQueryResult()
                {
                    ReservationID = entities[i].GetTypedColumnValue<string>(columns["ReservationID"].Name),
                    SOID = entities[i].GetTypedColumnValue<string>(columns["SOID"].Name),
                    Username = entities[i].GetTypedColumnValue<string>(columns["Username"].Name),
                    StreetAddress1 = entities[i].GetTypedColumnValue<string>(columns["StreetAddress1"].Name),
                    PostCode = entities[i].GetTypedColumnValue<string>(columns["PostCode"].Name),
                    City = entities[i].GetTypedColumnValue<string>(columns["City"].Name),
                    State = entities[i].GetTypedColumnValue<string>(columns["State"].Name),
                    Country = entities[i].GetTypedColumnValue<string>(columns["Country"].Name),
                    EmailAddress = entities[i].GetTypedColumnValue<string>(columns["EmailAddress"].Name),
                    FaxNumber = entities[i].GetTypedColumnValue<string>(columns["FaxNumber"].Name),
                    PhoneNumber = entities[i].GetTypedColumnValue<string>(columns["PhoneNumber"].Name),
                    IMSIType = entities[i].GetTypedColumnValue<string>(columns["IMSIType"].Name),
                    LineID = lineId
                });

                var reservationId = entities[i].GetTypedColumnValue<string>(columns["ReservationID"].Name);
                var poItem = GetItem(entities[i].GetTypedColumnValue<Guid>(columns["Id"].Name), lineId, entities[i].GetTypedColumnValue<string>(columns["ReservationID"].Name), i + 1);
                if (poItem != null)
                {
                    request.productOrderItem.Add(poItem);
                }
            }

            var Admin2Name = entity.GetTypedColumnValue<string>(columns["Admin2Name"].Name);
            var street1 = entity.GetTypedColumnValue<string>(columns["StreetAddress1"].Name);
            var street2 = string.Empty;

            if (!string.IsNullOrEmpty(street1) && street1.Length > 200)
            {
                street2 = street1.Substring(200);
                street1 = street1.Substring(0, 200);
            }

            var externalReference = queryResult
                .GroupBy(item => new
                {
                    item.SOID
                })
                .Select(item =>
                {
                    var itemWithReservationID = item.Where(line => !string.IsNullOrEmpty(line.ReservationID)).ToList();

                    return new CPExternalReference()
                    {
                        externalIdentifierType = itemWithReservationID.FirstOrDefault().ReservationID,
                        id = item.Key.SOID
                    };
                })
                .ToList();

            var relatedParty = new List<CPRelatedParty>
            {
                new CPRelatedParty {
                    name = entity.GetTypedColumnValue<string>(columns["Admin1Name"].Name),
                    id = Masking(entity.GetTypedColumnValue<string>(columns["Admin1IdNo"].Name)),
                    role = "customerdetails",
                    contactMedium = new List<contactMedium> {
                        new contactMedium {
                            mediumType = "collated",
                            characteristic = new characteristic {
                                streetAddress1 = street1,
                                streetAddress2 = street2,
                                postcode = entity.GetTypedColumnValue<string>(columns["PostCode"].Name),
                                city = entity.GetTypedColumnValue<string>(columns["City"].Name),
                                state = entity.GetTypedColumnValue<string>(columns["State"].Name),
                                country = entity.GetTypedColumnValue<string>(columns["Country"].Name),
                                emailAddress = entity.GetTypedColumnValue<string>(columns["Admin1Email"].Name),
                                faxNumber = entity.GetTypedColumnValue<string>(columns["FaxNumber"].Name),
                                phoneNumber = entity.GetTypedColumnValue<string>(columns["Admin1Phone"].Name)
                            }
                        }
                    }
                }
            };

            if (!string.IsNullOrEmpty(Admin2Name))
            {
                var newParty = new CPRelatedParty
                {
                    name = Admin2Name,
                    id = Masking(entity.GetTypedColumnValue<string>(columns["Admin2IdNo"].Name)),
                    role = "altcustomerdetls",
                    contactMedium = new List<contactMedium>
                    {
                        new contactMedium {
                            mediumType = "collated",
                            characteristic = new characteristic {
                                streetAddress1 = street1,
                                streetAddress2 = street2,
                                postcode = entity.GetTypedColumnValue<string>(columns["PostCode"].Name),
                                city = entity.GetTypedColumnValue<string>(columns["City"].Name),
                                state = entity.GetTypedColumnValue<string>(columns["State"].Name),
                                country = entity.GetTypedColumnValue<string>(columns["Country"].Name),
                                emailAddress = entity.GetTypedColumnValue<string>(columns["Admin2Email"].Name),
                                faxNumber = entity.GetTypedColumnValue<string>(columns["FaxNumber"].Name),
                                phoneNumber = entity.GetTypedColumnValue<string>(columns["Admin2Phone"].Name)
                            }
                        }
                    }
                };
                relatedParty.Add(newParty);
            }
            else
            {
                var newParty = new CPRelatedParty
                {
                    name = entity.GetTypedColumnValue<string>(columns["Auth1Name"].Name),
                    id = Masking(entity.GetTypedColumnValue<string>(columns["Auth1IdNo"].Name)),
                    role = "altcustomerdetls",
                    contactMedium = new List<contactMedium>
                    {
                        new contactMedium {
                            mediumType = "collated",
                            characteristic = new characteristic {
                                streetAddress1 = street1,
                                streetAddress2 = street2,
                                postcode = entity.GetTypedColumnValue<string>(columns["PostCode"].Name),
                                city = entity.GetTypedColumnValue<string>(columns["City"].Name),
                                state = entity.GetTypedColumnValue<string>(columns["State"].Name),
                                country = entity.GetTypedColumnValue<string>(columns["Country"].Name),
                                emailAddress = entity.GetTypedColumnValue<string>(columns["Auth1Email"].Name),
                                faxNumber = entity.GetTypedColumnValue<string>(columns["FaxNumber"].Name),
                                phoneNumber = entity.GetTypedColumnValue<string>(columns["Auth1Phone"].Name)
                            }
                        }
                    }
                };
                relatedParty.Add(newParty);
            }

            var lineWithIMSI = queryResult.Where(item => !string.IsNullOrEmpty(item.IMSIType)).ToList();
            var additionalIndex = entities.Count;
            if (lineWithIMSI.Count > 0)
            {
                for (int i = 0; i < lineWithIMSI.Count; i++)
                {
                    additionalIndex++;
                    imsiItems.Add(new productOrderItem
                    {
                        id = additionalIndex.ToString(),
                        reservationId = lineWithIMSI[i].ReservationID ?? externalReference.FirstOrDefault().externalIdentifierType,
                        action = "add",
                        productOrderLineItem = new List<productOrderLineItem> {
                            new productOrderLineItem {
                                id = lineWithIMSI[i].LineID,
                                action = "add",
                                quantity = 1,
                                product = new product {
                                    id = "USI_200018342",
                                    name = "USI_200018342"
                                }
                            }
                        },
                        quantity = 1
                    });
                }
                request.productOrderItem.AddRange(imsiItems);
            }

            request.externalReference = externalReference;
            request.completionDate = DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd");
            request.relatedParty = relatedParty;
            request.note = new List<Note>
                {
                    new Note {
                        id = "customerName",
                        text = entity.GetTypedColumnValue<string>(columns["CustomerName"].Name)
                    },
                    new Note {
                        id = "customerId",
                        text = entity.GetTypedColumnValue<string>(columns["CustomerBRN"].Name)
                    }
                };

            return GetParam(request);
        }

        public virtual dynamic BuildQuery() {
            var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgLineDetail");
            var columns = new Dictionary<string, EntitySchemaQueryColumn>();

            columns.Add("Id", esq.AddColumn("Id"));
            columns.Add("ReservationID", esq.AddColumn("DgReservationID"));
            columns.Add("SOID", esq.AddColumn("DgSOID"));
            columns.Add("IMSIType", esq.AddColumn("DgOrderIMSIType.Name"));
            columns.Add("Username", esq.AddColumn("DgUsername"));
            columns.Add("LineID", esq.AddColumn("DgLineId"));

            columns.Add("StreetAddress1", esq.AddColumn("DgSubmission.DgCRMGroup.DgDeliveryaddress"));
            columns.Add("PostCode", esq.AddColumn("DgSubmission.DgCRMGroup.DgPostcodeAdmInformationDelivery.Name"));
            columns.Add("City", esq.AddColumn("DgSubmission.DgCRMGroup.DgCityAdmInformationDelivery.Name"));
            columns.Add("State", esq.AddColumn("DgSubmission.DgCRMGroup.DgStateAdmInfoDelivery.Name"));
            columns.Add("Country", esq.AddColumn("DgSubmission.DgCRMGroup.DgCountryAdmInformationDelivery.Name"));
            columns.Add("EmailAddress", esq.AddColumn("DgSubmission.DgCRMGroup.DgCompanyEmail"));
            columns.Add("FaxNumber", esq.AddColumn("DgSubmission.DgCRMGroup.DgFaxNo"));
            columns.Add("PhoneNumber", esq.AddColumn("DgSubmission.DgCRMGroup.DgTelNo"));
            columns.Add("CustomerName", esq.AddColumn("DgSubmission.DgCRMGroup.DgGroupName"));
            columns.Add("CustomerBRN", esq.AddColumn("DgSubmission.DgCRMGroup.DgBRN"));

            //customerdetails data
            columns.Add("Admin1Name", esq.AddColumn("DgSubmission.DgCRMGroup.DgAdministrationName1"));
            columns.Add("Admin1Email", esq.AddColumn("DgSubmission.DgCRMGroup.DgAdministrationEmail1"));
            columns.Add("Admin1Phone", esq.AddColumn("DgSubmission.DgCRMGroup.DgMobilePhone1"));
            columns.Add("Admin1IdNo", esq.AddColumn("DgSubmission.DgCRMGroup.DgIdNo1"));

            //Admin 2 data
            columns.Add("Admin2Name", esq.AddColumn("DgSubmission.DgCRMGroup.DgAdministrationName2"));
            columns.Add("Admin2Email", esq.AddColumn("DgSubmission.DgCRMGroup.DgAdministrationEmail2"));
            columns.Add("Admin2Phone", esq.AddColumn("DgSubmission.DgCRMGroup.DgMobilePhone2"));
            columns.Add("Admin2IdNo", esq.AddColumn("DgSubmission.DgCRMGroup.DgIdNo2"));

            //altcustomerdetails data
            columns.Add("Auth1Name", esq.AddColumn("DgSubmission.DgCRMGroup.DgAuthorizedName1"));
            columns.Add("Auth1Email", esq.AddColumn("DgSubmission.DgCRMGroup.DgAuthorizedEmail1"));
            columns.Add("Auth1Phone", esq.AddColumn("DgSubmission.DgCRMGroup.DgAuthorizedMobilePhone1"));
            columns.Add("Auth1IdNo", esq.AddColumn("DgSubmission.DgCRMGroup.DgAuthorizedIdNo1"));

            columns["LineID"].OrderByAsc(0);
            
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgIsMMAG", false));

            return new {
                esq,
                columns
            };
        }

        protected virtual List<productOrderItem> GetItemERP(string SOID) {
            var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgERPOrderInfo");
            var columns = new Dictionary<string, EntitySchemaQueryColumn>();

            columns.Add("MaterialCode", esq.AddColumn("DgMaterialCode"));
            columns.Add("ReservationID", esq.AddColumn("DgLineDetail.DgReservationID"));
            columns.Add("SOID", esq.AddColumn("DgLineDetail.DgSOID"));
            columns.Add("ERPLineID", esq.AddColumn("DgERPLineID"));
            columns.Add("NCCFLineID", esq.AddColumn("DgLineDetail.DgLineId"));

            columns["ERPLineID"].OrderByAsc(0);

            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgSOID", SOID));

            var result = new List<productOrderItem>();
            var entities = esq.GetEntityCollection(UserConnection);
            if (entities.Count == 0) {
                return result;
            }

            var productList = entities.SelectMany(item => {
                var products = new List<productOrderItem>();

                products.Add(new productOrderItem {
                    id = item.GetTypedColumnValue<string>(columns["ERPLineID"].Name),
                    action = "add",
                    quantity = 1,
                    reservationId = item.GetTypedColumnValue<string>(columns["ReservationID"].Name),
                    productOrderLineItem = new List<productOrderLineItem>() {
                        new productOrderLineItem {
                            action = "add",
                            id = item.GetTypedColumnValue<string>(columns["NCCFLineID"].Name),
                            product = new product {
                                id = item.GetTypedColumnValue<string>(columns["MaterialCode"].Name),
                                name = item.GetTypedColumnValue<string>(columns["MaterialCode"].Name)
                            },
                            quantity = 1
                        }
                    }
                });

                return products;
            }).ToList();

            return productList;
        }

        protected virtual productOrderItem GetItem(Guid LineDetailId, string LineId, string ReservationId, int increment)
        {
            var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "DgFeeDetail");
            var columns = new Dictionary<string, EntitySchemaQueryColumn>();

            columns.Add("ResModeID", esq.AddColumn("DgResModeID"));
            columns.Add("OfferID", esq.AddColumn("DgOfferID"));
            columns.Add("ReservationID", esq.AddColumn("DgLineDetail.DgReservationID"));
            columns.Add("SOID", esq.AddColumn("DgLineDetail.DgSOID"));
            columns.Add("LineID", esq.AddColumn("DgLineDetail.DgLineId"));

            columns["LineID"].OrderByAsc(0);

            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgLineDetail", LineDetailId));
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Equal, "DgFeeName", "Handset Fee"));
            esq.Filters.Add(esq.CreateFilterWithParameters(FilterComparisonType.Greater, "DgSuppOfferIndex", 0));

            var entities = esq.GetEntityCollection(UserConnection);
            if (entities.Count == 0)
            {
                return null;
            }

            var result = new productOrderItem
            {
                id = increment.ToString(),
                reservationId = ReservationId,
                action = "add"
            };

            var deviceList = new List<DeviceItem>();
            foreach (var entity in entities)
            {
                deviceList.Add(new DeviceItem
                {
                    DeviceID = entity.GetTypedColumnValue<string>(columns["ResModeID"].Name),
                    OfferID = entity.GetTypedColumnValue<string>(columns["OfferID"].Name)
                });
            }
            result.productOrderLineItem = deviceList
                .GroupBy(item => new
                {
                    item.DeviceID,
                    item.OfferID
                })
                .Select(item => new productOrderLineItem
                {
                    id = LineId,
                    action = "add",
                    quantity = 1,
                    product = new product
                    {
                        id = item.Key.DeviceID,
                        name = item.Key.DeviceID
                    }
                })
                .ToList();
            result.quantity = deviceList.Count;

            return result;
        }

        protected virtual string Masking(string Text)
		{
			if (Text.Length <= 4) {
				return Text;
			}
			
			string lastFour = Text.Substring(Text.Length - 4);
			return new string('*', Text.Length - 4) + lastFour;
		}
    }

    public class CPQueryResult
    {
        public string ResModeID { get; set; }
        public string OfferID { get; set; }
        public string ReservationID { get; set; }
        public string SOID { get; set; }
        public string Username { get; set; }
        public string StreetAddress1 { get; set; }
        public string PostCode { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Country { get; set; }
        public string EmailAddress { get; set; }
        public string FaxNumber { get; set; }
        public string PhoneNumber { get; set; }
        public string IMSIType { get; set; }
        public string LineID { get; set; }
    }

    public class DeviceItem
    {
        public string DeviceID { get; set; }
        public string OfferID { get; set; }
    }
}