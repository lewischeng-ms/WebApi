﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using System.Web.OData.Builder;
using System.Web.OData.Extensions;
using System.Web.OData.Routing;
using System.Web.OData.Routing.Conventions;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Library;
using Microsoft.OData.Edm.Library.Values;
using Microsoft.TestCommon;
using Newtonsoft.Json.Linq;

namespace System.Web.OData.Formatter
{
    public class ODataFunctionTests
    {
        private const string BaseAddress = @"http://localhost/";

        private const string PrimitiveValues = "(intValues=@p)?@p=[1, 2, null, 7, 8]";

        private const string ComplexValue1 = "{\"@odata.type\":\"%23NS.Address\",\"Street\":\"NE 24th St.\",\"City\":\"Redmond\"}";
        private const string ComplexValue2 = "{\"@odata.type\":\"%23NS.SubAddress\",\"Street\":\"LianHua Rd.\",\"City\":\"Shanghai\", \"Code\":9.9}";

        private const string ComplexValue = "(address=@p)?@p=" + ComplexValue1;
        private const string CollectionComplex = "(addresses=@p)?@p=[" + ComplexValue1 + "," + ComplexValue2 + "]";

        private const string EntityValue1 = "{\"@odata.type\":\"%23NS.Customer\",\"Id\":91,\"Name\":\"John\",\"Location\":" + ComplexValue1 + "}";
        private const string EntityValue2 = "{\"@odata.type\":\"%23NS.SpecialCustomer\",\"Id\":92,\"Name\":\"Mike\",\"Location\":" + ComplexValue2 + ",\"Title\":\"883F50C5-F554-4C49-98EA-F7CACB41658C\"}";

        private const string EntityValue = "(customer=@p)?@p=" + EntityValue1;
        private const string CollectionEntity = "(customers=@p)?@p={\"value\":[" + EntityValue1 + "," + EntityValue2 + "]}";

        private const string EntityReference = "(customer=@p)?@p={\"@odata.id\":\"http://localhost/odata/FCustomers(8)\"}";

        private const string EntityReferences =
            "(customers=@p)?@p={\"value\":[{\"@odata.id\":\"http://localhost/odata/FCustomers(81)\"},{\"@odata.id\":\"http://localhost/odata/FCustomers(82)/NS.SpecialCustomer\"}]}";

        private readonly HttpClient _client;

        public ODataFunctionTests()
        {
            DefaultODataPathHandler pathHandler = new DefaultODataPathHandler();
            HttpConfiguration configuration =
                new[] { typeof(MetadataController), typeof(FCustomersController) }.GetHttpConfiguration();
            var model = GetUnTypedEdmModel();

            // without attribute routing
            configuration.MapODataServiceRoute("odata1", "odata", model, pathHandler, ODataRoutingConventions.CreateDefault());

            // only with attribute routing
            IList<IODataRoutingConvention> routingConventions = new List<IODataRoutingConvention>
            {
                new AttributeRoutingConvention(model, configuration)
            };
            configuration.MapODataServiceRoute("odata2", "attribute", model, pathHandler, routingConventions);

            _client = new HttpClient(new HttpServer(configuration));
        }

        public static TheoryDataSet<string> BoundFunctionRouteData
        {
            get
            {
                return new TheoryDataSet<string>
                {
                    { GetBoundFunction("IntCollectionFunction", PrimitiveValues) },

                    { GetBoundFunction("ComplexFunction", ComplexValue) },

                    { GetBoundFunction("ComplexCollectionFunction", CollectionComplex) },

                    { GetBoundFunction("EntityFunction", EntityValue) },
                    { GetBoundFunction("EntityFunction", EntityReference) },// reference

                    { GetBoundFunction("CollectionEntityFunction", CollectionEntity) },
                    { GetBoundFunction("CollectionEntityFunction", EntityReferences) },// references
                };
            }
        }

        public static TheoryDataSet<string> UnboundFunctionRouteData
        {
            get
            {
                return new TheoryDataSet<string>
                {
                    { GetUnboundFunction("UnboundIntCollectionFunction", PrimitiveValues) },

                    { GetUnboundFunction("UnboundComplexFunction", ComplexValue) },

                    { GetUnboundFunction("UnboundComplexCollectionFunction", CollectionComplex) },

                    { GetUnboundFunction("UnboundEntityFunction", EntityValue) },
                    { GetUnboundFunction("UnboundEntityFunction", EntityReference) },// reference

                    { GetUnboundFunction("UnboundCollectionEntityFunction", CollectionEntity) },
                    { GetUnboundFunction("UnboundCollectionEntityFunction", EntityReferences) }, // references
                };
            }
        }

        private static string GetUnboundFunction(string functionName, string parameter)
        {
            int key = 9;
            if (parameter.Contains("@odata.id"))
            {
                key = 8; // used to check the result
            }

            parameter = parameter.Insert(1, "key=" + key + ",");
            return functionName + parameter;
        }

        private static string GetBoundFunction(string functionName, string parameter)
        {
            int key = 9;
            if (parameter.Contains("@odata.id"))
            {
                key = 8; // used to check the result
            }

            return "FCustomers(" + key + ")/NS." + functionName + parameter;
        }

        [Theory]
        [PropertyData("BoundFunctionRouteData")]
        public void FunctionWorks_WithParameters_ForUnTyped(string odataPath)
        {
            // Arrange
            string requestUri = BaseAddress + "odata/" + odataPath;

            // Act
            var response = _client.GetAsync(requestUri).Result;

            // Assert
            response.EnsureSuccessStatusCode();
            dynamic result = JObject.Parse(response.Content.ReadAsStringAsync().Result);
            Assert.True((bool)result["value"]);
        }

        [Theory]
        [PropertyData("BoundFunctionRouteData")]
        [PropertyData("UnboundFunctionRouteData")]
        public void FunctionWorks_WithParameters_OnlyWithAttributeRouting_ForUnTyped(string odataPath)
        {
            // Arrange
            string requestUri = BaseAddress + "attribute/" + odataPath;
            if (requestUri.Contains("@odata.id"))
            {
                requestUri = requestUri.Replace("http://localhost/odata", "http://localhost/attribute");
            }

            // Act
            var response = _client.GetAsync(requestUri).Result;

            // Assert
            response.EnsureSuccessStatusCode();
            dynamic result = JObject.Parse(response.Content.ReadAsStringAsync().Result);
            Assert.True((bool)result["value"]);
        }

        private static IEdmModel GetUnTypedEdmModel()
        {
            EdmModel model = new EdmModel();

            // Enum type "Color"
            EdmEnumType colorEnum = new EdmEnumType("NS", "Color");
            colorEnum.AddMember(new EdmEnumMember(colorEnum, "Red", new EdmIntegerConstant(0)));
            colorEnum.AddMember(new EdmEnumMember(colorEnum, "Blue", new EdmIntegerConstant(1)));
            colorEnum.AddMember(new EdmEnumMember(colorEnum, "Green", new EdmIntegerConstant(2)));
            model.AddElement(colorEnum);

            // complex type "Address"
            EdmComplexType address = new EdmComplexType("NS", "Address");
            address.AddStructuralProperty("Street", EdmPrimitiveTypeKind.String);
            address.AddStructuralProperty("City", EdmPrimitiveTypeKind.String);
            model.AddElement(address);

            // derived complex type "SubAddress"
            EdmComplexType subAddress = new EdmComplexType("NS", "SubAddress", address);
            subAddress.AddStructuralProperty("Code", EdmPrimitiveTypeKind.Double);
            model.AddElement(subAddress);

            // entity type "Customer"
            EdmEntityType customer = new EdmEntityType("NS", "Customer");
            customer.AddKeys(customer.AddStructuralProperty("Id", EdmPrimitiveTypeKind.Int32));
            customer.AddStructuralProperty("Name", EdmPrimitiveTypeKind.String);
            customer.AddStructuralProperty("Location", new EdmComplexTypeReference(address, isNullable: true));
            model.AddElement(customer);

            // derived entity type special customer
            EdmEntityType specialCustomer = new EdmEntityType("NS", "SpecialCustomer", customer);
            specialCustomer.AddStructuralProperty("Title", EdmPrimitiveTypeKind.Guid);
            model.AddElement(specialCustomer);

            // entity sets
            EdmEntityContainer container = new EdmEntityContainer("NS", "Default");
            model.AddElement(container);
            container.AddEntitySet("FCustomers", customer);

            EdmComplexTypeReference complexType = new EdmComplexTypeReference(address, isNullable: false);
            EdmCollectionTypeReference complexCollectionType = new EdmCollectionTypeReference(new EdmCollectionType(complexType));

            EdmEntityTypeReference entityType = new EdmEntityTypeReference(customer, isNullable: false);
            EdmCollectionTypeReference entityCollectionType = new EdmCollectionTypeReference(new EdmCollectionType(entityType));

            IEdmTypeReference intType = EdmCoreModel.Instance.GetPrimitive(EdmPrimitiveTypeKind.Int32, isNullable: true);
            EdmCollectionTypeReference primitiveCollectionType = new EdmCollectionTypeReference(new EdmCollectionType(intType));

            // bound functions
            BoundFunction(model, "IntCollectionFunction", "intValues", primitiveCollectionType, entityType);

            BoundFunction(model, "ComplexFunction", "address", complexType, entityType);

            BoundFunction(model, "ComplexCollectionFunction", "addresses", complexCollectionType, entityType);

            BoundFunction(model, "EntityFunction", "customer", entityType, entityType);

            BoundFunction(model, "CollectionEntityFunction", "customers", entityCollectionType, entityType);

            // unbound functions
            UnboundFunction(container, "UnboundIntCollectionFunction", "intValues", primitiveCollectionType);

            UnboundFunction(container, "UnboundComplexFunction", "address", complexType);

            UnboundFunction(container, "UnboundComplexCollectionFunction", "addresses", complexCollectionType);

            UnboundFunction(container, "UnboundEntityFunction", "customer", entityType);

            UnboundFunction(container, "UnboundCollectionEntityFunction", "customers", entityCollectionType);

            model.SetAnnotationValue<BindableProcedureFinder>(model, new BindableProcedureFinder(model));
            return model;
        }

        private static void BoundFunction(EdmModel model, string funcName, string paramName, IEdmTypeReference edmType, IEdmEntityTypeReference bindingType)
        {
            IEdmTypeReference returnType = EdmCoreModel.Instance.GetPrimitive(EdmPrimitiveTypeKind.Boolean, isNullable: false);

            EdmFunction boundFunction = new EdmFunction("NS", funcName, returnType, isBound: true, entitySetPathExpression: null, isComposable: false);
            boundFunction.AddParameter("entity", bindingType);
            boundFunction.AddParameter(paramName, edmType);
            model.AddElement(boundFunction);
        }

        private static void UnboundFunction(EdmEntityContainer container, string funcName, string paramName, IEdmTypeReference edmType)
        {
            IEdmTypeReference returnType = EdmCoreModel.Instance.GetPrimitive(EdmPrimitiveTypeKind.Boolean, isNullable: false);

            var unboundFunction = new EdmFunction("NS", funcName, returnType, isBound: false, entitySetPathExpression: null, isComposable: true);
            unboundFunction.AddParameter("key", EdmCoreModel.Instance.GetPrimitive(EdmPrimitiveTypeKind.Int32, isNullable: false));
            unboundFunction.AddParameter(paramName, edmType);
            container.AddFunctionImport(funcName, unboundFunction, entitySet: null);
        }
    }

    public class FCustomersController : ODataController
    {
        [HttpGet]
        [ODataRoute("FCustomers({key})/NS.IntCollectionFunction(intValues={intValues})")]
        [ODataRoute("UnboundIntCollectionFunction(key={key},intValues={intValues})")]
        public bool IntCollectionFunction(int key, [FromODataUri] IEnumerable<int?> intValues)
        {
            Assert.NotNull(intValues);

            IList<int?> values = intValues.ToList();
            Assert.Equal(1, values[0]);
            Assert.Equal(2, values[1]);
            Assert.Null(values[2]);
            Assert.Equal(7, values[3]);
            Assert.Equal(8, values[4]);

            return true;
        }

        [HttpGet]
        [ODataRoute("FCustomers({key})/NS.ComplexFunction(address={address})")]
        [ODataRoute("UnboundComplexFunction(key={key},address={address})")]
        public bool ComplexFunction(int key, [FromODataUri] EdmComplexObject address)
        {
            Assert.NotNull(address);
            dynamic result = address;
            Assert.Equal("NS.Address", address.GetEdmType().FullName());
            Assert.Equal("NE 24th St.", result.Street);
            Assert.Equal("Redmond", result.City);
            return true;
        }

        [HttpGet]
        [ODataRoute("FCustomers({key})/NS.ComplexCollectionFunction(addresses={addresses})")]
        [ODataRoute("UnboundComplexCollectionFunction(key={key},addresses={addresses})")]
        public bool ComplexCollectionFunction(int key, [FromODataUri] EdmComplexObjectCollection addresses)
        {
            Assert.NotNull(addresses);
            IList<IEdmComplexObject> results = addresses.ToList();

            Assert.Equal(2, results.Count);

            // #1
            EdmComplexObject complex = results[0] as EdmComplexObject;
            Assert.Equal("NS.Address", complex.GetEdmType().FullName());

            dynamic address = results[0];
            Assert.NotNull(address);
            Assert.Equal("NE 24th St.", address.Street);
            Assert.Equal("Redmond", address.City);

            // #2
            complex = results[1] as EdmComplexObject;
            Assert.Equal("NS.SubAddress", complex.GetEdmType().FullName());

            address = results[1];
            Assert.NotNull(address);
            Assert.Equal("LianHua Rd.", address.Street);
            Assert.Equal("Shanghai", address.City);
            Assert.Equal(9.9, address.Code);
            return true;
        }

        [HttpGet]
        [ODataRoute("FCustomers({key})/NS.EntityFunction(customer={customer})")]
        [ODataRoute("UnboundEntityFunction(key={key},customer={customer})")]
        public bool EntityFunction(int key, [FromODataUri] EdmEntityObject customer)
        {
            Assert.NotNull(customer);
            dynamic result = customer;
            Assert.Equal("NS.Customer", customer.GetEdmType().FullName());

            // entity call
            if (key == 9)
            {
                Assert.Equal(91, result.Id);
                Assert.Equal("John", result.Name);

                dynamic address = result.Location;
                EdmComplexObject addressObj = Assert.IsType<EdmComplexObject>(address);
                Assert.Equal("NS.Address", addressObj.GetEdmType().FullName());
                Assert.Equal("NE 24th St.", address.Street);
                Assert.Equal("Redmond", address.City);
            }
            else
            {
                // entity reference call
                Assert.Equal(8, result.Id);
                Assert.Equal("Id", String.Join(",", customer.GetChangedPropertyNames()));

                Assert.Equal("Name,Location", String.Join(",", customer.GetUnchangedPropertyNames()));
            }

            return true;
        }

        [HttpGet]
        [ODataRoute("FCustomers({key})/NS.CollectionEntityFunction(customers={customers})")]
        [ODataRoute("UnboundCollectionEntityFunction(key={key},customers={customers})")]
        public bool CollectionEntityFunction(int key, [FromODataUri] EdmEntityObjectCollection customers)
        {
            Assert.NotNull(customers);
            IList<IEdmEntityObject> results = customers.ToList();
            Assert.Equal(2, results.Count);

            // entities call
            if (key == 9)
            {
                // #1
                EdmEntityObject entity = results[0] as EdmEntityObject;
                Assert.NotNull(entity);
                Assert.Equal("NS.Customer", entity.GetEdmType().FullName());

                dynamic customer = results[0];
                Assert.Equal(91, customer.Id);
                Assert.Equal("John", customer.Name);

                dynamic address = customer.Location;
                EdmComplexObject addressObj = Assert.IsType<EdmComplexObject>(address);
                Assert.Equal("NS.Address", addressObj.GetEdmType().FullName());
                Assert.Equal("NE 24th St.", address.Street);
                Assert.Equal("Redmond", address.City);

                // #2
                entity = results[1] as EdmEntityObject;
                Assert.Equal("NS.SpecialCustomer", entity.GetEdmType().FullName());

                customer = results[1];
                Assert.Equal(92, customer.Id);
                Assert.Equal("Mike", customer.Name);

                address = customer.Location;
                addressObj = Assert.IsType<EdmComplexObject>(address);
                Assert.Equal("NS.SubAddress", addressObj.GetEdmType().FullName());
                Assert.Equal("LianHua Rd.", address.Street);
                Assert.Equal("Shanghai", address.City);
                Assert.Equal(9.9, address.Code);

                Assert.Equal(new Guid("883F50C5-F554-4C49-98EA-F7CACB41658C"), customer.Title);
            }
            else
            {
                // entity references call
                int id = 81;
                foreach (IEdmEntityObject edmObj in results)
                {
                    EdmEntityObject entity = edmObj as EdmEntityObject;
                    Assert.NotNull(entity);
                    Assert.Equal("NS.Customer", entity.GetEdmType().FullName());

                    dynamic customer = entity;
                    Assert.Equal(id++, customer.Id);
                    Assert.Equal("Id", String.Join(",", customer.GetChangedPropertyNames()));
                    Assert.Equal("Name,Location", String.Join(",", customer.GetUnchangedPropertyNames()));
                }
            }

            return true;
        }
    }
}