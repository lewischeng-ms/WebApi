﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Web.Http;
using System.Web.OData.Formatter;
using System.Web.OData.Properties;
using System.Web.OData.Query;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Vocabularies;

namespace System.Web.OData.Builder
{
    /// <summary>
    /// <see cref="EdmTypeBuilder"/> builds <see cref="IEdmType"/>'s from <see cref="StructuralTypeConfiguration"/>'s.
    /// </summary>
    internal class EdmTypeBuilder
    {
        private readonly List<IEdmTypeConfiguration> _configurations;
        private readonly Dictionary<Type, IEdmType> _types = new Dictionary<Type, IEdmType>();
        private readonly Dictionary<PropertyInfo, IEdmProperty> _properties = new Dictionary<PropertyInfo, IEdmProperty>();
        private readonly Dictionary<IEdmProperty, QueryableRestrictions> _propertiesRestrictions = new Dictionary<IEdmProperty, QueryableRestrictions>();
        private readonly Dictionary<IEdmProperty, ModelBoundQuerySettings> _propertiesQuerySettings = new Dictionary<IEdmProperty, ModelBoundQuerySettings>();
        private readonly Dictionary<IEdmStructuredType, ModelBoundQuerySettings> _structuredTypeQuerySettings = new Dictionary<IEdmStructuredType, ModelBoundQuerySettings>();
        private readonly Dictionary<Enum, IEdmEnumMember> _members = new Dictionary<Enum, IEdmEnumMember>();
        private readonly Dictionary<IEdmStructuredType, PropertyInfo> _openTypes = new Dictionary<IEdmStructuredType, PropertyInfo>();

        internal EdmTypeBuilder(IEnumerable<IEdmTypeConfiguration> configurations)
        {
            _configurations = configurations.ToList();
        }

        private Dictionary<Type, IEdmType> GetEdmTypes()
        {
            // Reset
            _types.Clear();
            _properties.Clear();
            _members.Clear();
            _openTypes.Clear();

            // Create headers to allow CreateEdmTypeBody to blindly references other things.
            foreach (IEdmTypeConfiguration config in _configurations)
            {
                CreateEdmTypeHeader(config);
            }

            foreach (IEdmTypeConfiguration config in _configurations)
            {
                CreateEdmTypeBody(config);
            }

            foreach (StructuralTypeConfiguration structrual in _configurations.OfType<StructuralTypeConfiguration>())
            {
                CreateNavigationProperty(structrual);
            }

            return _types;
        }

        private void CreateEdmTypeHeader(IEdmTypeConfiguration config)
        {
            IEdmType edmType = GetEdmType(config.ClrType);
            if (edmType == null)
            {
                if (config.Kind == EdmTypeKind.Complex)
                {
                    ComplexTypeConfiguration complex = (ComplexTypeConfiguration)config;
                    IEdmComplexType baseType = null;
                    if (complex.BaseType != null)
                    {
                        CreateEdmTypeHeader(complex.BaseType);
                        baseType = GetEdmType(complex.BaseType.ClrType) as IEdmComplexType;

                        Contract.Assert(baseType != null);
                    }

                    EdmComplexType complexType = new EdmComplexType(config.Namespace, config.Name,
                        baseType, complex.IsAbstract ?? false, complex.IsOpen);

                    _types.Add(config.ClrType, complexType);

                    if (complex.IsOpen)
                    {
                        // add a mapping between the open complex type and its dynamic property dictionary.
                        _openTypes.Add(complexType, complex.DynamicPropertyDictionary);
                    }
                    edmType = complexType;
                }
                else if (config.Kind == EdmTypeKind.Entity)
                {
                    EntityTypeConfiguration entity = config as EntityTypeConfiguration;
                    Contract.Assert(entity != null);

                    IEdmEntityType baseType = null;
                    if (entity.BaseType != null)
                    {
                        CreateEdmTypeHeader(entity.BaseType);
                        baseType = GetEdmType(entity.BaseType.ClrType) as IEdmEntityType;

                        Contract.Assert(baseType != null);
                    }

                    EdmEntityType entityType = new EdmEntityType(config.Namespace, config.Name, baseType,
                        entity.IsAbstract ?? false, entity.IsOpen, entity.HasStream);
                    _types.Add(config.ClrType, entityType);

                    if (entity.IsOpen)
                    {
                        // add a mapping between the open entity type and its dynamic property dictionary.
                        _openTypes.Add(entityType, entity.DynamicPropertyDictionary);
                    }
                    edmType = entityType;
                }
                else
                {
                    EnumTypeConfiguration enumTypeConfiguration = config as EnumTypeConfiguration;

                    // The config has to be enum.
                    Contract.Assert(enumTypeConfiguration != null);

                    _types.Add(enumTypeConfiguration.ClrType,
                        new EdmEnumType(enumTypeConfiguration.Namespace, enumTypeConfiguration.Name,
                            GetTypeKind(enumTypeConfiguration.UnderlyingType), enumTypeConfiguration.IsFlags));
                }
            }

            IEdmStructuredType structuredType = edmType as IEdmStructuredType;
            StructuralTypeConfiguration structuralTypeConfiguration = config as StructuralTypeConfiguration;
            if (structuredType != null && structuralTypeConfiguration != null &&
                !_structuredTypeQuerySettings.ContainsKey(structuredType))
            {
                ModelBoundQuerySettings querySettings =
                    structuralTypeConfiguration.QueryConfiguration.ModelBoundQuerySettings;
                if (querySettings != null)
                {
                    _structuredTypeQuerySettings.Add(structuredType,
                        structuralTypeConfiguration.QueryConfiguration.ModelBoundQuerySettings);
                }
            }
        }

        private void CreateEdmTypeBody(IEdmTypeConfiguration config)
        {
            IEdmType edmType = GetEdmType(config.ClrType);

            if (edmType.TypeKind == EdmTypeKind.Complex)
            {
                CreateComplexTypeBody((EdmComplexType)edmType, (ComplexTypeConfiguration)config);
            }
            else if (edmType.TypeKind == EdmTypeKind.Entity)
            {
                CreateEntityTypeBody((EdmEntityType)edmType, (EntityTypeConfiguration)config);
            }
            else
            {
                Contract.Assert(edmType.TypeKind == EdmTypeKind.Enum);
                CreateEnumTypeBody((EdmEnumType)edmType, (EnumTypeConfiguration)config);
            }
        }

        private void CreateStructuralTypeBody(EdmStructuredType type, StructuralTypeConfiguration config)
        {
            foreach (PropertyConfiguration property in config.Properties)
            {
                IEdmProperty edmProperty = null;

                switch (property.Kind)
                {
                    case PropertyKind.Primitive:
                        PrimitivePropertyConfiguration primitiveProperty = (PrimitivePropertyConfiguration)property;
                        EdmPrimitiveTypeKind typeKind = primitiveProperty.TargetEdmTypeKind ??
                                                        GetTypeKind(primitiveProperty.PropertyInfo.PropertyType);
                        IEdmTypeReference primitiveTypeReference = EdmCoreModel.Instance.GetPrimitive(
                            typeKind,
                            primitiveProperty.OptionalProperty);

                        edmProperty = type.AddStructuralProperty(
                            primitiveProperty.Name,
                            primitiveTypeReference,
                            defaultValue: null);
                        break;

                    case PropertyKind.Complex:
                        ComplexPropertyConfiguration complexProperty = property as ComplexPropertyConfiguration;
                        IEdmComplexType complexType = GetEdmType(complexProperty.RelatedClrType) as IEdmComplexType;

                        edmProperty = type.AddStructuralProperty(
                            complexProperty.Name,
                            new EdmComplexTypeReference(complexType, complexProperty.OptionalProperty));
                        break;

                    case PropertyKind.Collection:
                        edmProperty = CreateStructuralTypeCollectionPropertyBody(type, (CollectionPropertyConfiguration)property);
                        break;

                    case PropertyKind.Enum:
                        edmProperty = CreateStructuralTypeEnumPropertyBody(type, (EnumPropertyConfiguration)property);
                        break;

                    default:
                        break;
                }

                if (edmProperty != null)
                {
                    if (property.PropertyInfo != null)
                    {
                        _properties[property.PropertyInfo] = edmProperty;
                    }

                    if (property.IsRestricted)
                    {
                        _propertiesRestrictions[edmProperty] = new QueryableRestrictions(property);
                    }

                    if (property.QueryConfiguration.ModelBoundQuerySettings != null)
                    {
                        _propertiesQuerySettings.Add(edmProperty, property.QueryConfiguration.ModelBoundQuerySettings);
                    }
                }
            }
        }

        private IEdmProperty CreateStructuralTypeCollectionPropertyBody(EdmStructuredType type, CollectionPropertyConfiguration collectionProperty)
        {
            IEdmTypeReference elementTypeReference = null;
            Type clrType = TypeHelper.GetUnderlyingTypeOrSelf(collectionProperty.ElementType);

            if (clrType.IsEnum)
            {
                IEdmType edmType = GetEdmType(clrType);

                if (edmType == null)
                {
                    throw Error.InvalidOperation(SRResources.EnumTypeDoesNotExist, clrType.Name);
                }

                IEdmEnumType enumElementType = (IEdmEnumType)edmType;
                bool isNullable = collectionProperty.ElementType != clrType;
                elementTypeReference = new EdmEnumTypeReference(enumElementType, isNullable);
            }
            else
            {
                IEdmType edmType = GetEdmType(collectionProperty.ElementType);
                if (edmType != null)
                {
                    IEdmComplexType elementType = edmType as IEdmComplexType;
                    Contract.Assert(elementType != null);
                    elementTypeReference = new EdmComplexTypeReference(elementType, collectionProperty.OptionalProperty);
                }
                else
                {
                    elementTypeReference =
                        EdmLibHelpers.GetEdmPrimitiveTypeReferenceOrNull(collectionProperty.ElementType);
                    Contract.Assert(elementTypeReference != null);
                }
            }

            return type.AddStructuralProperty(
                collectionProperty.Name,
                new EdmCollectionTypeReference(new EdmCollectionType(elementTypeReference)));
        }

        private IEdmProperty CreateStructuralTypeEnumPropertyBody(EdmStructuredType type, EnumPropertyConfiguration enumProperty)
        {
            Type enumPropertyType = TypeHelper.GetUnderlyingTypeOrSelf(enumProperty.RelatedClrType);
            IEdmType edmType = GetEdmType(enumPropertyType);

            if (edmType == null)
            {
                throw Error.InvalidOperation(SRResources.EnumTypeDoesNotExist, enumPropertyType.Name);
            }

            IEdmEnumType enumType = (IEdmEnumType)edmType;
            IEdmTypeReference enumTypeReference = new EdmEnumTypeReference(enumType, enumProperty.OptionalProperty);

            return type.AddStructuralProperty(
                enumProperty.Name,
                enumTypeReference,
                defaultValue: null);
        }

        private void CreateComplexTypeBody(EdmComplexType type, ComplexTypeConfiguration config)
        {
            Contract.Assert(type != null);
            Contract.Assert(config != null);

            CreateStructuralTypeBody(type, config);
        }

        private void CreateEntityTypeBody(EdmEntityType type, EntityTypeConfiguration config)
        {
            Contract.Assert(type != null);
            Contract.Assert(config != null);

            CreateStructuralTypeBody(type, config);
            IEnumerable<IEdmStructuralProperty> keys = config.Keys.Select(p => type.DeclaredProperties.OfType<IEdmStructuralProperty>().First(dp => dp.Name == p.Name));
            type.AddKeys(keys);

            // Add the Enum keys
            keys = config.EnumKeys.Select(p => type.DeclaredProperties.OfType<IEdmStructuralProperty>().First(dp => dp.Name == p.Name));
            type.AddKeys(keys);
        }

        private void CreateNavigationProperty(StructuralTypeConfiguration config)
        {
            Contract.Assert(config != null);

            EdmStructuredType type = (EdmStructuredType)(GetEdmType(config.ClrType));

            foreach (NavigationPropertyConfiguration navProp in config.NavigationProperties)
            {
                EdmNavigationPropertyInfo info = new EdmNavigationPropertyInfo
                {
                    Name = navProp.Name,
                    TargetMultiplicity = navProp.Multiplicity,
                    Target = GetEdmType(navProp.RelatedClrType) as IEdmEntityType,
                    ContainsTarget = navProp.ContainsTarget,
                    OnDelete = navProp.OnDeleteAction
                };

                // Principal properties
                if (navProp.PrincipalProperties.Any())
                {
                    info.PrincipalProperties = GetDeclaringPropertyInfo(navProp.PrincipalProperties);
                }

                // Dependent properties
                if (navProp.DependentProperties.Any())
                {
                    info.DependentProperties = GetDeclaringPropertyInfo(navProp.DependentProperties);
                }

                IEdmProperty edmProperty = type.AddUnidirectionalNavigation(info);
                if (navProp.PropertyInfo != null && edmProperty != null)
                {
                    _properties[navProp.PropertyInfo] = edmProperty;
                }

                if (edmProperty != null)
                {
                    if (navProp.IsRestricted)
                    {
                        _propertiesRestrictions[edmProperty] = new QueryableRestrictions(navProp);
                    }

                    if (navProp.QueryConfiguration.ModelBoundQuerySettings != null)
                    {
                        _propertiesQuerySettings.Add(edmProperty, navProp.QueryConfiguration.ModelBoundQuerySettings);
                    }
                }
            }
        }

        private IList<IEdmStructuralProperty> GetDeclaringPropertyInfo(IEnumerable<PropertyInfo> propertyInfos)
        {
            IList<IEdmProperty> edmProperties = new List<IEdmProperty>();
            foreach (PropertyInfo propInfo in propertyInfos)
            {
                IEdmProperty edmProperty;
                if (_properties.TryGetValue(propInfo, out edmProperty))
                {
                    edmProperties.Add(edmProperty);
                }
                else
                {
                    Contract.Assert(propInfo.ReflectedType != null);
                    Type baseType = propInfo.ReflectedType.BaseType;
                    while (baseType != null)
                    {
                        PropertyInfo basePropInfo = baseType.GetProperty(propInfo.Name);
                        if (_properties.TryGetValue(basePropInfo, out edmProperty))
                        {
                            edmProperties.Add(edmProperty);
                            break;
                        }

                        baseType = baseType.BaseType;
                    }

                    Contract.Assert(baseType != null);
                }
            }

            return edmProperties.OfType<IEdmStructuralProperty>().ToList();
        }

        private void CreateEnumTypeBody(EdmEnumType type, EnumTypeConfiguration config)
        {
            Contract.Assert(type != null);
            Contract.Assert(config != null);

            foreach (EnumMemberConfiguration member in config.Members)
            {
                // EdmIntegerConstant can only support a value of long type.
                long value;
                try
                {
                    value = Convert.ToInt64(member.MemberInfo, CultureInfo.InvariantCulture);
                }
                catch
                {
                    throw Error.Argument("value", SRResources.EnumValueCannotBeLong, Enum.GetName(member.MemberInfo.GetType(), member.MemberInfo));
                }

                EdmEnumMember edmMember = new EdmEnumMember(type, member.Name,
                    new EdmEnumMemberValue(value));
                type.AddMember(edmMember);
                _members[member.MemberInfo] = edmMember;
            }
        }

        private IEdmType GetEdmType(Type clrType)
        {
            Contract.Assert(clrType != null);

            IEdmType edmType;
            _types.TryGetValue(clrType, out edmType);

            return edmType;
        }

        /// <summary>
        /// Builds <see cref="IEdmType"/> and <see cref="IEdmProperty"/>'s from <paramref name="configurations"/>
        /// </summary>
        /// <param name="configurations">A collection of <see cref="IEdmTypeConfiguration"/>'s</param>
        /// <returns>The built dictionary of <see cref="StructuralTypeConfiguration"/>'s indexed by their backing CLR type,
        /// and dictionary of <see cref="StructuralTypeConfiguration"/>'s indexed by their backing CLR property info</returns>
        public static EdmTypeMap GetTypesAndProperties(IEnumerable<IEdmTypeConfiguration> configurations)
        {
            if (configurations == null)
            {
                throw Error.ArgumentNull("configurations");
            }

            EdmTypeBuilder builder = new EdmTypeBuilder(configurations);
            return new EdmTypeMap(builder.GetEdmTypes(),
                builder._properties,
                builder._propertiesRestrictions,
                builder._propertiesQuerySettings,
                builder._structuredTypeQuerySettings,
                builder._members,
                builder._openTypes);
        }

        /// <summary>
        /// Gets the <see cref="EdmPrimitiveTypeKind"/> that maps to the <see cref="Type"/>
        /// </summary>
        /// <param name="clrType">The clr type</param>
        /// <returns>The corresponding Edm primitive kind.</returns>
        public static EdmPrimitiveTypeKind GetTypeKind(Type clrType)
        {
            IEdmPrimitiveType primitiveType = EdmLibHelpers.GetEdmPrimitiveTypeOrNull(clrType);
            if (primitiveType == null)
            {
                throw Error.Argument("clrType", SRResources.MustBePrimitiveType, clrType.FullName);
            }

            return primitiveType.PrimitiveKind;
        }
    }
}
