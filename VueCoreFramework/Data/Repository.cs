﻿using Microsoft.EntityFrameworkCore;
using VueCoreFramework.Controllers;
using VueCoreFramework.Data.Attributes;
using VueCoreFramework.Extensions;
using VueCoreFramework.Models;
using VueCoreFramework.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Metadata;

namespace VueCoreFramework.Data
{
    /// <summary>
    /// Handles operations with an <see cref="ApplicationDbContext"/> for a particular class.
    /// </summary>
    /// <typeparam name="T">
    /// The class whose operations with the <see cref="ApplicationDbContext"/> are handled by this
    /// <see cref="Repository{T}"/>.
    /// </typeparam>
    public class Repository<T> : IRepository where T : class
    {
        private const string primaryKeyVMProperty = "primaryKeyProperty";
        private readonly ApplicationDbContext _context;
        private readonly bool _isMenuClass;

        private DbSet<T> items;

        /// <summary>
        /// The <see cref="IEntityType"/> of this Repository.
        /// </summary>
        public IEntityType EntityType { get; }

        /// <summary>
        /// The primary key <see cref="IProperty"/> of this Repository's entity type.
        /// </summary>
        public IProperty PrimaryKey { get; }

        /// <summary>
        /// The name of the ViewModel property which indicates the primary key. Constant.
        /// </summary>
        public string PrimaryKeyVMProperty { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="Repository{T}"/>.
        /// </summary>
        /// <param name="context">The <see cref="ApplicationDbContext"/> wrapped by this <see cref="Repository{T}"/>.</param>
        public Repository(ApplicationDbContext context)
        {
            _context = context;
            EntityType = GetEntityType(context, typeof(T));
            _isMenuClass = typeof(T).GetTypeInfo().GetCustomAttribute<MenuClassAttribute>() != null;
            PrimaryKey = EntityType.FindPrimaryKey().Properties.FirstOrDefault();
            PrimaryKeyVMProperty = primaryKeyVMProperty;

            items = _context.Set<T>();
        }

        /// <summary>
        /// Asynchronously creates a new instance of <see cref="T"/> and adds it to the <see
        /// cref="ApplicationDbContext"/> instance.
        /// </summary>
        /// <param name="childProp">
        /// An optional navigation property which will be set on the new object.
        /// </param>
        /// <param name="parentId">
        /// The primary key of the entity which will be set on the <paramref name="childProp"/> property, as a string.
        /// </param>
        /// <returns>A ViewModel instance representing the newly added entity.</returns>
        public async Task<IDictionary<string, object>> AddAsync(PropertyInfo childProp, string parentId)
        {
            var item = typeof(T).GetConstructor(Type.EmptyTypes).Invoke(new object[] { });

            if (childProp != null && !string.IsNullOrEmpty(parentId))
            {
                var parentKey = GetPrimaryKeyFromString(childProp.PropertyType, parentId);
                EntityType.FindNavigation(childProp)
                    .ForeignKey.Properties.FirstOrDefault()
                    .PropertyInfo.SetValue(item, parentKey);
            }

            items.Add(item as T);
            await _context.SaveChangesAsync();

            return await GetViewModelAsync(_context, item as T);
        }

        /// <summary>
        /// Asynchronously adds an assortment of child entities to a parent entity under the given
        /// navigation property.
        /// </summary>
        /// <param name="id">The primary key of the parent entity, as a string.</param>
        /// <param name="childProp">The navigation property to which the children will be added.</param>
        /// <param name="childIds">The primary keys of the child entities which will be added, as strings.</param>
        public async Task AddChildrenToCollectionAsync(string id, PropertyInfo childProp, IEnumerable<string> childIds)
        {
            var mtmEntityType = EntityType.FindNavigation(childProp).GetTargetType();
            var mtmType = mtmEntityType.ClrType;

            var ptInfo = childProp.PropertyType.GetTypeInfo();
            var add = ptInfo.GetGenericTypeDefinition()
                        .MakeGenericType(mtmType)
                        .GetMethod("Add");

            var mtmCon = mtmType.GetConstructor(Type.EmptyTypes);

            var navs = mtmEntityType.GetNavigations();
            var mtmParentNav = navs.FirstOrDefault(n => n.FindInverse().Name == childProp.Name);
            var mtmChildNav = navs.FirstOrDefault(n => n != mtmParentNav);

            var parentPK = GetPrimaryKeyFromString(id);
            var parent = await FindItemWithPKAsync(parentPK);

            foreach (var childId in childIds)
            {
                var mtm = mtmCon.Invoke(new object[] { });
                var childPK = GetPrimaryKeyFromString(mtmChildNav.ClrType, childId);
                mtmChildNav.ForeignKey.Properties.FirstOrDefault().PropertyInfo.SetValue(mtm, childPK);
                mtmParentNav.ForeignKey.Properties.FirstOrDefault().PropertyInfo.SetValue(mtm, parentPK);
                add.Invoke(childProp.GetValue(parent), new object[] { mtm });
            }

            await _context.SaveChangesAsync();
        }

        private static bool AnyPropMatch(T item, string search)
        {
            var type = typeof(T);
            foreach (var pInfo in type.GetProperties())
            {
                // Only strings and numbers are checked, to avoid any
                // potentially expensive ToString operations on
                // potentially many rows.
                if (pInfo.PropertyType == typeof(string))
                {
                    if ((pInfo.GetValue(item) as string).IndexOf(search) != -1)
                    {
                        return true;
                    }
                }
                else if (pInfo.PropertyType == typeof(short)
                    || pInfo.PropertyType == typeof(int)
                    || pInfo.PropertyType == typeof(long)
                    || pInfo.PropertyType == typeof(float)
                    || pInfo.PropertyType == typeof(double)
                    || pInfo.PropertyType == typeof(decimal))
                {
                    if (pInfo.GetValue(item).ToString().IndexOf(search) != -1)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Finds an entity with the given primary key value and returns a ViewModel for that entity.
        /// If no entity is found, an empty ViewModel is returned (not null).
        /// </summary>
        /// <param name="id">The primary key of the entity to be found, as a string.</param>
        /// <returns>A ViewModel representing the item found, or an empty ViewModel if none is found.</returns>
        public async Task<IDictionary<string, object>> FindAsync(string id)
        {
            var key = GetPrimaryKeyFromString(id);
            var item = await items.FindAsync(key);
            return await GetViewModelAsync(_context, item);
        }

        /// <summary>
        /// Finds an entity with the given primary key value. If no entity is found, then null is returned.
        /// </summary>
        /// <param name="id">The primary key of the entity to be found, as a string.</param>
        /// <returns>
        /// The item found, or null if none is found.
        /// </returns>
        public async Task<object> FindItemAsync(string id)
        {
            var key = GetPrimaryKeyFromString(id);
            return await FindItemWithPKAsync(key);
        }

        /// <summary>
        /// Finds an entity with the given primary key value. If no entity is found, then null is returned.
        /// </summary>
        /// <param name="id">The primary key of the entity to be found.</param>
        /// <returns>
        /// The item found, or null if none is found.
        /// </returns>
        public async Task<object> FindItemWithPKAsync(object key)
        {
            var item = await items.FindAsync(key);
            if (item != null)
            {
                foreach (var nav in _context.Entry(item).Navigations)
                {
                    nav.Load();
                }
            }
            return item;
        }

        /// <summary>
        /// Enumerates all the entities in the <see cref="ApplicationDbContext"/>'s set, returning a
        /// ViewModel representing each.
        /// </summary>
        /// <returns>ViewModels representing the items in the set.</returns>
        public async Task<IList<IDictionary<string, object>>> GetAllAsync()
        {
            IList<IDictionary<string, object>> all = new List<IDictionary<string, object>>();
            foreach (var item in items)
            {
                all.Add(await GetViewModelAsync(_context, item));
            }
            return all;
        }

        /// <summary>
        /// Finds the primary keys of all child entities in the given relationship, as strings.
        /// </summary>
        /// <param name="id">The primary key of the parent entity, as a string.</param>
        /// <param name="childProp">The navigation property of the relationship.</param>
        public async Task<IList<string>> GetAllChildIdsAsync(string id, PropertyInfo childProp)
        {
            var item = await FindItemAsync(id);
            var coll = _context.Entry(item).Collection(childProp.Name);
            await coll.LoadAsync();
            var childPKProp = coll.Metadata.GetTargetType().FindPrimaryKey().Properties.FirstOrDefault().PropertyInfo;
            IList<string> childIds = new List<string>();
            foreach (var child in coll.CurrentValue)
            {
                childIds.Add(childPKProp.GetValue(child).ToString());
            }
            return childIds;
        }

        /// <summary>
        /// Finds the primary key of a child entity in the given relationship, as a string.
        /// </summary>
        /// <param name="id">The primary key of the parent entity, as a string.</param>
        /// <param name="childProp">The navigation property of the relationship.</param>
        public async Task<string> GetChildIdAsync(string id, PropertyInfo childProp)
        {
            var item = await FindItemAsync(id);
            var nav = _context.Entry(item).Navigation(childProp.Name);
            await nav.LoadAsync();
            var child = childProp.GetValue(item);
            return GetPrimaryKey(nav.Metadata.GetTargetType().ClrType)
                .PropertyInfo.GetValue(child).ToString();
        }

        /// <summary>
        /// Calculates and enumerates the set of child entities in a given relationship with the
        /// given paging parameters, as ViewModels.
        /// </summary>
        /// <param name="dataType">The type of the parent entity.</param>
        /// <param name="id">The primary key of the parent entity, as a string.</param>
        /// <param name="childProp">The navigation property of the relationship on the parent entity.</param>
        /// <param name="search">
        /// An optional search term which will filter the results. Any string or numeric property
        /// with matching text will be included.
        /// </param>
        /// <param name="sortBy">
        /// An optional property name which will be used to sort the items before calculating the
        /// page contents.
        /// </param>
        /// <param name="descending">
        /// Indicates whether the sort is descending; if false, the sort is ascending.
        /// </param>
        /// <param name="page">The page number requested.</param>
        /// <param name="rowsPerPage">The number of items per page.</param>
        public async Task<IList<IDictionary<string, object>>> GetChildPageAsync(
            string id,
            PropertyInfo childProp,
            string search,
            string sortBy,
            bool descending,
            int page,
            int rowsPerPage,
            IList<Claim> claims)
        {
            var item = await FindItemAsync(id);
            var coll = _context.Entry(item).Collection(childProp.Name);
            await coll.LoadAsync();
            var childType = EntityType.FindNavigation(childProp).GetTargetType();
            var childRepo = (IRepository)Activator.CreateInstance(typeof(Repository<>).MakeGenericType(childType.ClrType), _context);
            if (EntityType.FindNavigation(childProp.Name).FindInverse().IsCollection())
            {
                var navs = childType.GetNavigations();
                var mtmParentNav = navs.FirstOrDefault(n => n.FindInverse() == childProp);
                var mtmChildProp = navs.FirstOrDefault(n => n != mtmParentNav).PropertyInfo;

                return await childRepo.GetPageItemsAsync(
                    coll.CurrentValue.Cast<object>().Select(c => mtmChildProp.GetValue(c)).AsQueryable(),
                    search, sortBy, descending, page, rowsPerPage, claims);
            }
            else
            {
                return await childRepo.GetPageItemsAsync(
                    coll.CurrentValue.Cast<object>().AsQueryable(),
                    search, sortBy, descending, page, rowsPerPage, claims);
            }
        }

        /// <summary>
        /// Retrieves the total number of child entities in the given relationship.
        /// </summary>
        /// <param name="id">The primary key of the parent entity, as a string.</param>
        /// <param name="childProp">The navigation property of the relationship on the parent entity.</param>
        /// <returns>
        /// A <see cref="long"/> that represents the total number of children in the relationship.
        /// </returns>
        public async Task<long> GetChildTotalAsync(string id, PropertyInfo childProp)
        {
            var item = await FindItemAsync(id);
            await _context.Entry(item).Collection(childProp.Name).LoadAsync();
            var children = childProp.GetValue(item) as IEnumerable<object>;
            return children.LongCount();
        }

        private static IEntityType GetEntityType(ApplicationDbContext context, Type type)
            => context.Model.FindEntityType(type.FullName);

        private FieldDefinition GetFieldDefinition(PropertyInfo pInfo)
        {
            var fd = new FieldDefinition
            {
                // The name is converted to initial-lower-case for use in the SPA framework.
                Model = pInfo.Name.ToInitialLower()
            };

            var entityPInfo = EntityType.FindProperty(pInfo);

            // Keys are always hidden in the SPA framework.
            if (entityPInfo != null
                && (entityPInfo.IsPrimaryKey() || entityPInfo.IsForeignKey()))
            {
                fd.Type = "label";
                fd.HideInTable = true;
                fd.Visible = false;
                return fd;
            }

            // If the property is fully hidden, there is no need to identify its usual type or other
            // attributes, since it will never be visible.
            var hidden = pInfo.GetCustomAttribute<HiddenAttribute>();
            if (hidden?.Hidden == true)
            {
                fd.Type = "label";
                fd.HideInTable = true;
                fd.Visible = false;
                return fd;
            }

            fd.HideInTable = hidden?.HideInTable;

            // Navigation properties use special fields.
            var nav = EntityType.FindNavigation(pInfo);
            if (nav != null)
            {
                // The input type for navigation properties is the type name.
                fd.InputType = nav.GetTargetType().Name;

                var inverse = nav.FindInverse();
                fd.InverseType = inverse.Name;

                if (nav.IsCollection())
                {
                    // Children in a many-to-many relationship are manipulated in a table containing
                    // all the items of the child type, where items can be added to or removed from
                    // the parent's collection.
                    if (inverse.IsCollection())
                    {
                        fd.Type = "objectMultiSelect";
                    }
                    // Children in a one-to-many relationship are manipulated in a table containing
                    // only those items in the parent's collection. Adding or removing items to/from
                    // the collection is accomplished by creating new items or deleting them (which
                    // only deletes them fully when appropriate). This handles cases where the child
                    // objects are nested child objects, child objects with multiple parent
                    // relationships, and also items which are MenuClass types in their own right.
                    else
                    {
                        fd.Type = "objectCollection";
                    }
                }
                // Reverse-navigation properties only allow view/edit. No adding/deleting, since the
                // child object shouldn't add/delete a parent.
                else if (nav.IsDependentToPrincipal())
                {
                    fd.Type = "objectReference";
                }
                // Children in a many-to-one relationship (i.e. which can have more than one
                // parent) can be selected from a list, as well as added/edited/deleted.
                else if (inverse.IsCollection())
                {
                    fd.Type = "objectSelect";
                }
                // Children in a one-to-one relationship are treated as purely nested objects, and
                // can only be added, edited, and deleted, to prevent any child from being referenced
                // by more than one parent inappropriately. In fact the child may have other
                // relationships which result in it not being purely nested, or even be a MenuClass
                // item in its own right, but for this relationship the controls make no assumptions.
                else
                {
                    fd.Type = "object";
                }
            }
            else
            {
                var dataType = pInfo.GetCustomAttribute<DataTypeAttribute>();
                var step = pInfo.GetCustomAttribute<StepAttribute>();
                var ptInfo = pInfo.PropertyType.GetTypeInfo();

                if (!string.IsNullOrEmpty(dataType?.CustomDataType))
                {
                    if (dataType.CustomDataType == "Color")
                    {
                        fd.Type = "vuetifyColor";
                    }
                    // Any custom data type not recognized as one of the special types handled above is
                    // treated as a simple text field.
                    else
                    {
                        fd.Type = "vuetifyText";
                        fd.InputType = "text";
                        fd.Validator = "string";
                    }
                }
                else if (dataType != null)
                {
                    switch (dataType.DataType)
                    {
                        case DataType.Currency:
                            fd.Type = "vuetifyText";
                            fd.InputType = "number";
                            if (step != null)
                            {
                                fd.Step = Math.Abs(step.Step);
                            }
                            else
                            {
                                // If a step isn't specified, currency uses cents by default.
                                fd.Step = 0.01;
                            }
                            fd.Validator = "number";
                            break;
                        case DataType.Date:
                            fd.Type = "vuetifyDateTime";
                            fd.InputType = "date";
                            break;
                        case DataType.DateTime:
                            fd.Type = "vuetifyDateTime";
                            fd.InputType = "dateTime";
                            break;
                        case DataType.Time:
                            fd.Type = "vuetifyDateTime";
                            fd.InputType = "time";
                            break;
                        case DataType.Duration:
                            fd.Type = "vuetifyTimespan";
                            var formatAttr = pInfo.GetCustomAttribute<DisplayFormatAttribute>();
                            fd.InputType = formatAttr?.DataFormatString;
                            fd.Validator = "timespan";
                            if (step != null)
                            {
                                fd.Step = Math.Abs(step.Step);
                            }
                            else
                            {
                                // If a step isn't specified, duration uses milliseconds by default.
                                fd.Step = 0.001;
                            }
                            break;
                        case DataType.EmailAddress:
                            fd.Type = "vuetifyText";
                            fd.InputType = "email";
                            fd.Validator = "email";
                            break;
                        case DataType.MultilineText:
                            fd.Type = "vuetifyText";
                            fd.InputType = "textArea";
                            fd.Validator = "string";
                            break;
                        case DataType.Password:
                            fd.Type = "vuetifyText";
                            fd.InputType = "password";
                            fd.Validator = "string";
                            break;
                        case DataType.PhoneNumber:
                            fd.Type = "vuetifyText";
                            fd.InputType = "telephone";
                            // This regex is a permissive test for U.S. phone numbers, accepting letters
                            // and most forms of "ext", but not invalid numbers (e.g. too short, too
                            // long, or with invalid registers).
                            fd.Pattern = @"1?(?:[.\s-]?[2-9]\d{2}[.\s-]?|\s?\([2-9]\d{2}\)\s?)(?:[1-9]\d{2}[.\s-]?\d{4}\s?(?:\s?([xX]|[eE][xX]|[eE][xX]\.|[eE][xX][tT]|[eE][xX][tT]\.)\s?\d{3,4})?|[a-zA-Z]{7})";
                            fd.Validator = "string_regexp";
                            break;
                        case DataType.PostalCode:
                            fd.Type = "vuetifyText";
                            fd.InputType = "text";
                            // This regex accepts both short and long U.S. postal codes.
                            fd.Pattern = @"(^(?!0{5})(\d{5})(?!-?0{4})(|-\d{4})?$)";
                            fd.Validator = "string_regexp";
                            break;
                        case DataType.ImageUrl:
                        case DataType.Url:
                            fd.Type = "vuetifyText";
                            fd.InputType = "url";
                            fd.Validator = "string";
                            break;
                        default:
                            // If a data type was specified but not one of those recognized, it is
                            // treated as a simple text field.
                            fd.Type = "vuetifyText";
                            fd.InputType = "text";
                            fd.Validator = "string";
                            break;
                    }
                }
                // If a data type isn't specified explicitly, the type is determined by the Type of the property.
                else if (pInfo.PropertyType == typeof(string))
                {
                    fd.Type = "vuetifyText";
                    fd.InputType = "text";
                    fd.Validator = "string";
                }
                else if (pInfo.PropertyType == typeof(bool)
                    || Nullable.GetUnderlyingType(pInfo.PropertyType) == typeof(bool))
                {
                    fd.Type = "vuetifyCheckbox";
                }
                else if (pInfo.PropertyType == typeof(DateTime))
                {
                    fd.Type = "vuetifyDateTime";
                    fd.InputType = "dateTime";
                }
                else if (pInfo.PropertyType == typeof(TimeSpan)
                    || Nullable.GetUnderlyingType(pInfo.PropertyType) == typeof(TimeSpan))
                {
                    fd.Type = "vuetifyTimespan";
                    var formatAttr = pInfo.GetCustomAttribute<DisplayFormatAttribute>();
                    fd.InputType = formatAttr?.DataFormatString;
                    fd.Validator = "timespan";
                    if (step != null)
                    {
                        fd.Step = Math.Abs(step.Step);
                    }
                    else
                    {
                        // If a step isn't specified, duration uses milliseconds by default.
                        fd.Step = 0.001;
                    }
                }
                else if (ptInfo.IsEnum)
                {
                    fd.Type = "vuetifySelect";
                    if (ptInfo.GetCustomAttribute<FlagsAttribute>() == null)
                    {
                        // Non-Flags enums are handled with single-selects.
                        fd.InputType = "single";
                    }
                    else
                    {
                        // Flags enums are handled with multiselects.
                        fd.InputType = "multiple";
                    }
                    if (fd.Values == null)
                    {
                        fd.Values = new List<ChoiceOption>();
                    }
                    foreach (var value in Enum.GetValues(pInfo.PropertyType))
                    {
                        fd.Values.Add(new ChoiceOption
                        {
                            // The display text for each option is set to the enum value's description
                            // (name if one isn't explicitly specified).
                            Text = EnumExtensions.GetDescription(pInfo.PropertyType, value),
                            Value = (int)value
                        });
                    }
                }
                else if (pInfo.PropertyType.IsNumeric())
                {
                    fd.Type = "vuetifyText";
                    fd.InputType = "number";
                    if (step != null)
                    {
                        if (pInfo.PropertyType.IsIntegralNumeric())
                        {
                            // If a step is specified for an integer-type numeric type, ensure it is not
                            // less than 1, and is an integer.
                            fd.Step = Math.Max(1, Math.Abs(Math.Round(step.Step)));
                        }
                        else
                        {
                            // If a step is specified for a real-type numeric type, ensure it is not
                            // equal to or less than 0.
                            fd.Step = Math.Max(double.Epsilon, Math.Abs(step.Step));
                        }
                    }
                    else
                    {
                        if (pInfo.PropertyType.IsRealNumeric())
                        {
                            fd.Step = 0.1;
                        }
                        else
                        {
                            fd.Step = 1;
                        }
                    }
                    fd.Validator = "number";
                }
                // Guids (when not used as keys or hidden) are shown as labels, since editing is not
                // presumed to be valid.
                else if (pInfo.PropertyType == typeof(Guid)
                    || Nullable.GetUnderlyingType(pInfo.PropertyType) == typeof(Guid))
                {
                    fd.Type = "label";
                }
                // Unrecognized types are represented as plain labels.
                else
                {
                    fd.Type = "label";
                }
            }

            var display = pInfo.GetCustomAttribute<DisplayAttribute>();
            fd.GroupName = display?.GetGroupName();
            fd.Hint = display?.GetDescription();
            fd.Label = display?.GetName();
            fd.Placeholder = display?.GetPrompt();

            // If no label or placeholder text was set, the property name is used.
            if (fd.Label == null && fd.Placeholder == null)
            {
                if (fd.Type == "vuetifyText" || fd.Type == "vuetifyCheckbox"
                    || fd.Type == "vuetifySelect" || fd.Type == "vuetifyDateTime")
                {
                    // For most Vuetify fields, the placeholder is used.
                    fd.Placeholder = pInfo.Name;
                }
                else
                {
                    // For other field types, the label is used.
                    fd.Label = pInfo.Name;
                }
            }

            fd.Help = pInfo.GetCustomAttribute<HelpAttribute>()?.HelpText;

            fd.Required = nav != null
                ? nav.IsDependentToPrincipal() && nav.ForeignKey.IsRequired
                : !entityPInfo.IsNullable;

            if (pInfo.GetCustomAttribute<EditableAttribute>()?.AllowEdit == false)
            {
                if (fd.Type == "vuetifyText")
                {
                    // Non-editable text fields are marked read-only.
                    fd.Readonly = true;
                }
                else
                {
                    // Other non-editable field types are disabled.
                    fd.Disabled = true;
                }
            }

            fd.Default = pInfo.GetCustomAttribute<DefaultAttribute>()?.Default;

            var range = pInfo.GetCustomAttribute<RangeAttribute>();
            fd.Min = range?.Minimum;
            fd.Max = range?.Maximum;

            var pattern = pInfo.GetCustomAttribute<RegularExpressionAttribute>();
            if (!string.IsNullOrEmpty(pattern?.Pattern))
            {
                fd.Pattern = pattern.Pattern;
                // Any field with an explicit pattern automatically gets the regex validator (unless
                // another one is explicitly set, which will override this later).
                fd.Validator = "string_regexp";
            }

            // Icons are only checked for relevant field types.
            if (fd.Type == "vuetifyText" || fd.Type == "vuetifyCheckbox" || fd.Type == "vuetifySelect")
            {
                fd.Icon = pInfo.GetCustomAttribute<IconAttribute>()?.Icon;
            }

            // Text field properties are only checked for text fields.
            if (fd.Type == "vuetifyText")
            {
                var textAttr = pInfo.GetCustomAttribute<TextAttribute>();
                fd.Prefix = textAttr?.Prefix;
                fd.Suffix = textAttr?.Suffix;
                fd.Rows = textAttr?.Rows;
                if (fd.Rows < 1)
                {
                    // Row amounts less than 1 are invalid, so the specified amount is disregarded.
                    fd.Rows = null;
                }
                if (fd.Rows > 1)
                {
                    // A row amount greater than 1 automatically indicates a textarea even if the
                    // property wasn't explicitly marked as such with a datatype attribute.
                    fd.InputType = "textArea";
                }
            }

            fd.Validator = pInfo.GetCustomAttribute<ValidatorAttribute>()?.Validator;

            return fd;
        }

        /// <summary>
        /// Generates and enumerates <see cref="FieldDefinition"/>s representing the properties of
        /// <see cref="T"/>.
        /// </summary>
        public IEnumerable<FieldDefinition> GetFieldDefinitions()
        {
            foreach (var pInfo in typeof(T).GetProperties())
            {
                yield return GetFieldDefinition(pInfo);
            }
        }

        /// <summary>
        /// Calculates and enumerates the set of entities with the given paging parameters, as ViewModels.
        /// </summary>
        /// <param name="search">
        /// An optional search term which will filter the results. Any string or numeric property
        /// with matching text will be included.
        /// </param>
        /// <param name="sortBy">
        /// An optional property name which will be used to sort the items before calculating the
        /// page contents.
        /// </param>
        /// <param name="descending">
        /// Indicates whether the sort is descending; if false, the sort is ascending.
        /// </param>
        /// <param name="page">The page number requested.</param>
        /// <param name="rowsPerPage">The number of items per page.</param>
        /// <param name="except">
        /// An enumeration of primary keys of items which should be excluded from the results before
        /// caluclating the page contents, as strings.
        /// </param>
        public async Task<IList<IDictionary<string, object>>> GetPageAsync(
            string search,
            string sortBy,
            bool descending,
            int page,
            int rowsPerPage,
            IEnumerable<string> except,
            IList<Claim> claims)
            => await GetPageItemsAsync(items.Where(i => !except.Contains(PrimaryKey.PropertyInfo.GetValue(i).ToString())),
                search, sortBy, descending, page, rowsPerPage, claims);

        /// <summary>
        /// Calculates and enumerates the given items with the given paging parameters, as ViewModels.
        /// </summary>
        /// <param name="items">The items to filter, sort, and page.</param>
        /// <param name="search">
        /// An optional search term which will filter the results. Any string or numeric property
        /// with matching text will be included.
        /// </param>
        /// <param name="sortBy">
        /// An optional property name which will be used to sort the items before calculating the
        /// page contents.
        /// </param>
        /// <param name="descending">
        /// Indicates whether the sort is descending; if false, the sort is ascending.
        /// </param>
        /// <param name="page">The page number requested.</param>
        /// <param name="rowsPerPage">The number of items per page.</param>
        /// <param name="except">
        /// An enumeration of primary keys of items which should be excluded from the results before
        /// caluclating the page contents.
        /// </param>
        public async Task<IList<IDictionary<string, object>>> GetPageItemsAsync(
            IQueryable<object> items,
            string search,
            string sortBy,
            bool descending,
            int page,
            int rowsPerPage,
            IList<Claim> claims)
        {
            var dataType = typeof(T).Name;

            IQueryable<T> filteredItems = items.Cast<T>().Where(i =>
               AuthorizationController.IsAuthorized(claims, dataType, CustomClaimTypes.PermissionDataView,
               PrimaryKey.PropertyInfo.GetValue(i).ToString()));

            if (!string.IsNullOrEmpty(search))
            {
                filteredItems = filteredItems.Where(i => AnyPropMatch(i, search));
            }

            if (!string.IsNullOrEmpty(sortBy))
            {
                var sortProp = typeof(T).GetProperty(sortBy);
                if (sortProp == null)
                {
                    throw new ArgumentException($"{sortBy} is not a valid property for this item.", nameof(sortBy));
                }
                if (descending)
                {
                    filteredItems = filteredItems.OrderByDescending(i => sortProp.GetValue(i));
                }
                else
                {
                    filteredItems = filteredItems.OrderBy(i => sortProp.GetValue(i));
                }
            }

            if (rowsPerPage > 0)
            {
                if (page < 1)
                {
                    throw new ArgumentException($"{nameof(page)} cannot be < 1 if {nameof(rowsPerPage)} is > 0.", nameof(page));
                }
                filteredItems = filteredItems.Skip((page - 1) * rowsPerPage).Take(rowsPerPage);
            }

            IList<IDictionary<string, object>> vms = new List<IDictionary<string, object>>();
            foreach (var item in filteredItems)
            {
                vms.Add(await GetViewModelAsync(_context, item));
            }
            return vms;
        }

        private static IProperty GetPrimaryKey(IEntityType entityType)
            => entityType.FindPrimaryKey().Properties.FirstOrDefault();

        private IProperty GetPrimaryKey(Type type) => GetPrimaryKey(GetEntityType(_context, type));

        private static object GetPrimaryKeyFromString(IEntityType entityType, string pk_string)
        {
            if (string.IsNullOrEmpty(pk_string))
            {
                throw new ArgumentNullException(nameof(pk_string));
            }
            var keyType = GetPrimaryKey(entityType).ClrType;
            return GetPrimaryKeyFromStringBase(keyType, pk_string);
        }

        private object GetPrimaryKeyFromString(Type type, string pk_string)
            => GetPrimaryKeyFromString(GetEntityType(_context, type), pk_string);

        /// <summary>
        /// Converts the given string into its equivalent primary key for this type.
        /// </summary>
        /// <param name="pk_string">The primary key to convert, as a string.</param>
        /// <returns>The primary key, as whatever type is defined by the entity.</returns>
        public object GetPrimaryKeyFromString(string pk_string)
        {
            if (string.IsNullOrEmpty(pk_string))
            {
                throw new ArgumentNullException(nameof(pk_string));
            }
            var keyType = PrimaryKey.ClrType;
            return GetPrimaryKeyFromStringBase(keyType, pk_string);
        }

        private static object GetPrimaryKeyFromStringBase(Type keyType, string pk_string)
        {
            if (keyType == typeof(Guid))
            {
                if (Guid.TryParse(pk_string, out Guid guid))
                {
                    return guid;
                }
                else
                {
                    throw new ArgumentException($"The primary key of {typeof(T).Name} is Guid, and {nameof(pk_string)} is not a valid Guid.", nameof(pk_string));
                }
            }
            else if (keyType == typeof(int))
            {
                if (int.TryParse(pk_string, out int intKey))
                {
                    return intKey;
                }
                else
                {
                    throw new ArgumentException($"The primary key of {typeof(T).Name} is int, and {nameof(pk_string)} is not a valid Guid.", nameof(pk_string));
                }
            }
            else if (keyType == typeof(long))
            {
                if (long.TryParse(pk_string, out long longKey))
                {
                    return longKey;
                }
                else
                {
                    throw new ArgumentException($"The primary key of {typeof(T).Name} is long, and {nameof(pk_string)} is not a valid Guid.", nameof(pk_string));
                }
            }
            else return pk_string;
        }

        /// <summary>
        /// Asynchronously returns a <see cref="long"/> that represents the total number of entities
        /// in the set.
        /// </summary>
        public async Task<long> GetTotalAsync() => await items.LongCountAsync();

        private static async Task<IDictionary<string, object>> GetViewModelAsync(ApplicationDbContext context, T item)
        {
            IDictionary<string, object> vm = new Dictionary<string, object>();

            var entityType = GetEntityType(context, typeof(T));
            var entry = item == null ? null : context.Entry(item);

            // Add a property to the VM which identifies the primary key.
            vm[primaryKeyVMProperty] =
                entityType.FindPrimaryKey().Properties.FirstOrDefault().Name;

            var tInfo = typeof(T).GetTypeInfo();
            foreach (var pInfo in tInfo.GetProperties())
            {
                var ptInfo = pInfo.PropertyType.GetTypeInfo();
                var dataType = pInfo.GetCustomAttribute<DataTypeAttribute>();

                var nav = entityType.FindNavigation(pInfo.Name);
                var entityProp = entityType.FindProperty(pInfo.Name);

                if (nav != null)
                {
                    if (entry != null)
                    {
                        await entry.Navigation(pInfo.Name).LoadAsync();
                    }

                    // Collection navigation properties are represented as placeholder text, varying
                    // depending on whether the collection is empty or not.
                    if (nav.IsCollection())
                    {
                        vm[pInfo.Name.ToInitialLower()] =
                            (entry != null && entry.Collection(pInfo.Name).CurrentValue.GetEnumerator().MoveNext()
                            ? "[...]"
                            : "[None]");
                    }
                    else
                    {
                        var value = item == null ? null : pInfo.GetValue(item);
                        vm[pInfo.Name.ToInitialLower()] =
                            (item == null || value == null ? "[None]" : value.ToString());
                    }
                }
                // Keys are always hidden in the SPA framework, but are still included in the
                // ViewModel since the framework must reference the keys in order to manage
                // relationships.
                else if (entityProp.IsKey() || entityProp.IsForeignKey())
                {
                    object value = item == null ? null : pInfo.GetValue(item);
                    var nullableType = Nullable.GetUnderlyingType(pInfo.PropertyType);
                    if (value != null && nullableType != null)
                    {
                        value = pInfo.PropertyType.GetProperty("Value").GetValue(value);
                    }
                    if (value == null)
                    {
                        vm[pInfo.Name.ToInitialLower()] = null;
                    }
                    else
                    {
                        vm[pInfo.Name.ToInitialLower()] = value.ToString();
                    }
                }
                // Enum properties are given their actual (integer) value, but are also given a
                // 'Formatted' property in the ViewModel which contains either the description, or
                // placeholder text for unrecognized values (e.g. combined Flags values). This
                // formatted value is used in data tables.
                else if (ptInfo.IsEnum)
                {
                    object value = item == null ? 0 : pInfo.GetValue(item);
                    var name = pInfo.Name.ToInitialLower();
                    vm[name] = (int)value;

                    var desc = EnumExtensions.GetDescription(pInfo.PropertyType, value);
                    vm[name + "Formatted"] = string.IsNullOrEmpty(desc) ? "[...]" : desc;
                }
                // Date properties are given their actual value, but are also given a 'Formatted'
                // property in the ViewModel which contains their short date formatted string. This
                // formatted value is used in data tables.
                else if (dataType?.DataType == DataType.Date)
                {
                    var name = pInfo.Name.ToInitialLower();
                    var value = item == null ? null : pInfo.GetValue(item);
                    vm[name] = value;
                    if (value == null)
                    {
                        vm[name + "Formatted"] = "[None]";
                    }
                    else
                    {
                        DateTime dt = (DateTime)value;
                        vm[name + "Formatted"] = dt.ToString("d");
                    }
                }
                // Time properties are given their actual value, but are also given a 'Formatted'
                // property in the ViewModel which contains their short time formatted string. This
                // formatted value is used in data tables.
                else if (dataType?.DataType == DataType.Time)
                {
                    var name = pInfo.Name.ToInitialLower();
                    var value = item == null ? null : pInfo.GetValue(item);
                    vm[name] = value;
                    if (value == null)
                    {
                        vm[name + "Formatted"] = "[None]";
                    }
                    else
                    {
                        DateTime dt = (DateTime)value;
                        vm[name + "Formatted"] = dt.ToString("t");
                    }
                }
                // DateTime properties which are not marked as either Date or Time are given their
                // actual value, but are also given a 'Formatted' property in the ViewModel which
                // contains their general formatted string. This formatted value is used in data tables.
                else if (dataType?.DataType == DataType.DateTime || pInfo.PropertyType == typeof(DateTime))
                {
                    var name = pInfo.Name.ToInitialLower();
                    var value = item == null ? null : pInfo.GetValue(item);
                    vm[name] = value;
                    if (value == null)
                    {
                        vm[name + "Formatted"] = "[None]";
                    }
                    else
                    {
                        DateTime dt = (DateTime)value;
                        vm[name + "Formatted"] = dt.ToString("g");
                    }
                }
                // Duration properties are given their actual value, but are also given a 'Formatted'
                // property in the ViewModel which contains their formatted string. This formatted
                // value is used in data tables.
                else if (dataType?.DataType == DataType.Duration
                    || pInfo.PropertyType == typeof(TimeSpan)
                    || Nullable.GetUnderlyingType(pInfo.PropertyType) == typeof(TimeSpan))
                {
                    var name = pInfo.Name.ToInitialLower();
                    var value = item == null ? null : pInfo.GetValue(item);
                    vm[name] = value;
                    if (value == null)
                    {
                        vm[name + "Formatted"] = "[None]";
                    }
                    else
                    {
                        var ts = (TimeSpan)value;
                        vm[name + "Formatted"] = ts.ToString("c");
                    }
                }
                else
                {
                    object value = item == null ? null : pInfo.GetValue(item);
                    var nullableType = Nullable.GetUnderlyingType(pInfo.PropertyType);
                    if (value != null && nullableType != null)
                    {
                        value = nullableType.GetProperty("Value").GetValue(value);
                    }
                    // Null values are represented with placeholder text. The SPA framework
                    // automatically omits values with this placeholder text when sending data back
                    // for update, avoiding overwriting previously null values with the placeholder
                    // text inappropriately.
                    if (value == null)
                    {
                        vm[pInfo.Name.ToInitialLower()] = "[None]";
                    }
                    // Other recognized types are represented with their ToString equivalent.
                    else if (pInfo.PropertyType == typeof(string)
                        || pInfo.PropertyType.IsNumeric()
                        || pInfo.PropertyType == typeof(bool)
                        || Nullable.GetUnderlyingType(pInfo.PropertyType) == typeof(bool)
                        || pInfo.PropertyType == typeof(Guid)
                        || Nullable.GetUnderlyingType(pInfo.PropertyType) == typeof(Guid))
                    {
                        vm[pInfo.Name.ToInitialLower()] = value.ToString();
                    }
                    // Unsupported types are not displayed with toString, to avoid cases where this
                    // only shows the type name. Instead placeholder text is used for any value.
                    else
                    {
                        vm[pInfo.Name.ToInitialLower()] = "[...]";
                    }
                }
            }
            return vm;
        }

        /// <summary>
        /// Asynchronously removes an entity from the <see cref="ApplicationDbContext"/>.
        /// </summary>
        /// <param name="id">The primary key of the entity to remove, as a string.</param>
        public async Task RemoveAsync(string id)
        {
            var item = await FindItemAsync(id);
            await RemoveItemAsync(item as T);
        }

        private async Task RemoveItemAsync(T item)
        {
            items.Remove(item);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Asynchronously removes an assortment of child entities from a parent entity under the
        /// given navigation property.
        /// </summary>
        /// <param name="id">The primary key of the parent entity, as a string.</param>
        /// <param name="childProp">The navigation property from which the children will be removed.</param>
        /// <param name="childIds">The primary keys of the child entities which will be removed, as strings.</param>
        public async Task RemoveChildrenFromCollectionAsync(string id, PropertyInfo childProp, IEnumerable<string> childIds)
        {
            var mtmEntityType = EntityType.FindNavigation(childProp).GetTargetType();
            var parentPK = GetPrimaryKeyFromString(id);

            var navs = mtmEntityType.GetNavigations();
            var mtmParentNav = navs.FirstOrDefault(n => n.FindInverse().Name == childProp.Name);
            var mtmChildNav = navs.FirstOrDefault(n => n != mtmParentNav);

            foreach (var childId in childIds)
            {
                var childPK = GetPrimaryKeyFromString(mtmChildNav.ClrType, childId);
                var mtm = _context.Find(mtmEntityType.ClrType, parentPK, childPK);
                _context.Remove(mtm);
            }

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Asynchronously terminates a relationship bewteen two entities. If the child entity is
        /// made an orphan by the removal and is not a MenuClass object, it is then removed from the
        /// <see cref="ApplicationDbContext"/> entirely.
        /// </summary>
        /// <param name="id">The primary key of the child entity whose relationship is being severed, as a string.</param>
        /// <param name="childProp">The navigation property of the relationship being severed.</param>
        /// <returns>True if the item is removed from the <see cref="ApplicationDbContext"/>, false if not.</returns>
        public async Task<bool> RemoveFromParentAsync(string id, PropertyInfo childProp)
        {
            var childFK = EntityType.FindNavigation(childProp.Name).ForeignKey;
            var childFKProp = childFK.Properties.FirstOrDefault().PropertyInfo;

            // If this is a required relationship, removing from the parent is the same as deletion.
            if (childFK.IsRequired)
            {
                await RemoveAsync(id);
                return true;
            }

            // For non-required relationships, null the FK.
            var item = await FindItemAsync(id);
            childProp.SetValue(item, null);

            // If the child is not a MenuClass item, it should be removed if it's now an orphan (has
            // no remaining relationships).
            var orphan = false;
            if (!_isMenuClass)
            {
                // Check all navigation properties in the child item to see if it's an orphan.
                orphan = true;
                foreach (var nav in _context.Entry(item).Navigations)
                {
                    await nav.LoadAsync();
                    if (nav.CurrentValue != null)
                    {
                        orphan = false;
                        break;
                    }
                }
                // If the item is now an orphan, delete it.
                if (orphan)
                {
                    items.Remove(item as T);
                }
            }
            await _context.SaveChangesAsync();
            return orphan;
        }

        /// <summary>
        /// Asynchronously removes a collection of entities from the <see cref="ApplicationDbContext"/>.
        /// </summary>
        /// <param name="ids">An enumeration of the primary keys of the entities to remove, as strings.</param>
        public async Task RemoveRangeAsync(IEnumerable<string> ids)
        {
            items.RemoveRange(ids.Select(i => items.Find(GetPrimaryKeyFromString(i))));
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Asynchronously terminates a relationship for multiple entities. If any child entity is
        /// made an orphan by the removal and is not a MenuClass object, it is then removed from the
        /// <see cref="ApplicationDbContext"/> entirely.
        /// </summary>
        /// <param name="ids">
        /// An enumeration of primary keys of child entities whose relationships are being severed, as strings.
        /// </param>
        /// <param name="childProp">The navigation property of the relationship being severed.</param>
        /// <returns>A list of the Ids of any items removed from the <see cref="ApplicationDbContext"/>, as strings.</returns>
        public async Task<IList<string>> RemoveRangeFromParentAsync(IEnumerable<string> ids, PropertyInfo childProp)
        {
            var childFK = EntityType.FindNavigation(childProp.Name).ForeignKey;
            var childFKProp = childFK.Properties.FirstOrDefault().PropertyInfo;

            // If this is a required relationship, removing from the parent is the same as deletion.
            if (childFK.IsRequired)
            {
                await RemoveRangeAsync(ids);
                return ids.ToList();
            }

            // For non-required relationships, null the prop.
            IList<string> removedIds = new List<string>();
            foreach (var id in ids)
            {
                var item = await FindItemAsync(id);
                childProp.SetValue(item, null);

                // If the child is not a MenuClass item, it should be removed if it is now an orphan
                // (has no remaining relationships).
                if (!_isMenuClass)
                {
                    // Check all navigation properties in the child item to see if it's now an orphan.
                    var orphan = true;
                    foreach (var nav in _context.Entry(item).Navigations)
                    {
                        await nav.LoadAsync();
                        if (nav.CurrentValue != null)
                        {
                            orphan = false;
                            break;
                        }
                    }
                    // If the item is now an orphan, delete it.
                    if (orphan)
                    {
                        items.Remove(item as T);
                        removedIds.Add(id);
                    }
                }
            }
            await _context.SaveChangesAsync();
            return removedIds;
        }

        /// <summary>
        /// Asynchronously creates a relationship between two entities, replacing another entity
        /// which was previously in that relationship with another one. If the replaced entity is
        /// made an orphan by the removal and is not a MenuClass object, it is then removed from the
        /// <see cref="ApplicationDbContext"/> entirely.
        /// </summary>
        /// <param name="parentId">
        /// The primary key of the parent entity in the relationship, as a string.
        /// </param>
        /// <param name="newChildId">
        /// The primary key of the new child entity entering into the relationship, as a string.
        /// </param>
        /// <param name="childProp">The navigation property of the relationship on the child entity.</param>
        /// <returns>
        /// The Id of the removed child, if it is removed from the <see
        /// cref="ApplicationDbContext"/>, as a string; null if it is not.
        /// </returns>
        public async Task<string> ReplaceChildAsync(string parentId, string newChildId, PropertyInfo childProp)
        {
            var parentRepo = (IRepository)Activator.CreateInstance(typeof(Repository<>).MakeGenericType(childProp.PropertyType), _context);
            var parent = await parentRepo.FindItemAsync(parentId);
            var parentPK = parentRepo.GetPrimaryKeyFromString(parentId);

            var nav = EntityType.FindNavigation(childProp.Name);
            var oldChildId = PrimaryKey.PropertyInfo.GetValue(nav.FindInverse().PropertyInfo.GetValue(parent)).ToString();

            var newChild = await FindItemAsync(newChildId);
            nav.ForeignKey.Properties.FirstOrDefault().PropertyInfo.SetValue(newChild, parentPK);

            var removed = await RemoveFromParentAsync(oldChildId, childProp);
            return removed ? oldChildId : null;
        }

        /// <summary>
        /// Asynchronously creates a relationship between two entities, replacing another entity
        /// which was previously in that relationship with a new entity. If the replaced entity is
        /// made an orphan by the removal and is not a MenuClass object, it is then removed from the
        /// <see cref="ApplicationDbContext"/> entirely.
        /// </summary>
        /// <param name="parentId">The primary key of the parent entity in the relationship, as a string.</param>
        /// <param name="childProp">The navigation property of the relationship on the child entity.</param>
        public async Task<(IDictionary<string, object>, string)> ReplaceChildWithNewAsync(string parentId, PropertyInfo childProp)
        {
            var parentRepo = (IRepository)Activator.CreateInstance(typeof(Repository<>).MakeGenericType(childProp.PropertyType), _context);
            var parent = await parentRepo.FindItemAsync(parentId);

            var nav = EntityType.FindNavigation(childProp.Name);
            var oldChildId = PrimaryKey.PropertyInfo.GetValue(nav.FindInverse().PropertyInfo.GetValue(parent)).ToString();

            var newItem = await AddAsync(childProp, parentId);

            var removed = await RemoveFromParentAsync(oldChildId, childProp);

            return removed ? (newItem, oldChildId) : (newItem, (string)null);
        }

        /// <summary>
        /// Asynchronously updates an entity in the <see cref="ApplicationDbContext"/>. Returns a
        /// ViewModel representing the updated item.
        /// </summary>
        /// <param name="item">The item to update.</param>
        /// <returns>A ViewModel representing the updated item.</returns>
        public async Task<IDictionary<string, object>> UpdateAsync(object item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }
            items.Update(item as T);
            await _context.SaveChangesAsync();
            return await GetViewModelAsync(_context, item as T);
        }
    }
}
