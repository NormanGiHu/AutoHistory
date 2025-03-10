﻿// Copyright (c) Arch team. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Internal;

namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    /// Represents a plugin for Microsoft.EntityFrameworkCore to support automatically recording data changes history.
    /// </summary>
    public static class DbContextExtensions
    {
        /// <summary>
        /// Ensures the automatic history.
        /// </summary>
        /// <param name="context">The context.</param>
        public static void EnsureAutoHistory(this DbContext context)
        {
            EnsureAutoHistory<AutoHistory>(context, () => new AutoHistory());
        }

        public static void EnsureAutoHistory<TAutoHistory>(this DbContext context, Func<TAutoHistory> createHistoryFactory)
            where TAutoHistory : AutoHistory
        {
            // Must ToArray() here for excluding the AutoHistory model.
            // Currently, only support Modified and Deleted entity.
            var entries = context.ChangeTracker.Entries().Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted).ToArray();
            foreach (var entry in entries)
            {
                context.Add<TAutoHistory>(entry.AutoHistory(createHistoryFactory));
            }
        }

        internal static TAutoHistory AutoHistory<TAutoHistory>(this EntityEntry entry, Func<TAutoHistory> createHistoryFactory)
            where TAutoHistory : AutoHistory
        {
            var history = createHistoryFactory();
            history.TableName = entry.Metadata.GetTableName();

            // Get the mapped properties for the entity type.
            // (include shadow properties, not include navigations & references)
            var properties = entry.Properties;

            dynamic json = new System.Dynamic.ExpandoObject();
            switch (entry.State)
            {
                case EntityState.Added:
                    foreach (var prop in properties)
                    {
                        if (prop.Metadata.IsKey() || prop.Metadata.IsForeignKey())
                        {
                            continue;
                        }
                        ((IDictionary<String, Object>)json)[prop.Metadata.Name] = prop.CurrentValue != null
                            ? prop.CurrentValue
                            : null;
                    }

                    // REVIEW: what's the best way to set the RowId?
                    history.RowId = "0";
                    history.Kind = EntityState.Added;
                    history.Changed = JsonSerializer.Serialize(json);
                    break;
                case EntityState.Modified:
                    dynamic bef = new System.Dynamic.ExpandoObject();
                    dynamic aft = new System.Dynamic.ExpandoObject();

                    PropertyValues databaseValues = null;
                    foreach (var prop in properties)
                    {
                        if (prop.IsModified)
                        {
                            if (prop.OriginalValue != null)
                            {
                                if (!prop.OriginalValue.Equals(prop.CurrentValue))
                                {
                                    ((IDictionary<String, Object>)bef)[prop.Metadata.Name] = prop.OriginalValue;
                                }
                                else
                                {
                                    databaseValues = databaseValues ?? entry.GetDatabaseValues();
                                    var originalValue = databaseValues.GetValue<object>(prop.Metadata.Name);
                                    ((IDictionary<String, Object>)bef)[prop.Metadata.Name] = originalValue != null
                                        ? originalValue
                                        : null;
                                }
                            }
                            else
                            {
                                ((IDictionary<String, Object>)bef)[prop.Metadata.Name] = null;
                            }

                            ((IDictionary<String, Object>)aft)[prop.Metadata.Name] = prop.CurrentValue != null
                            ? prop.CurrentValue
                            : null;
                        }
                    }

                    ((IDictionary<String, Object>)json)["before"] = bef;
                    ((IDictionary<String, Object>)json)["after"] = aft;

                    history.RowId = entry.PrimaryKey();
                    history.Kind = EntityState.Modified;
                    history.Changed = JsonSerializer.Serialize(json);
                    break;
                case EntityState.Deleted:
                    foreach (var prop in properties)
                    {
                        ((IDictionary<String, Object>)json)[prop.Metadata.Name] = prop.OriginalValue != null
                            ? prop.OriginalValue
                            : null;
                    }
                    history.RowId = entry.PrimaryKey();
                    history.Kind = EntityState.Deleted;
                    history.Changed = JsonSerializer.Serialize(json);
                    break;
                case EntityState.Detached:
                case EntityState.Unchanged:
                default:
                    throw new NotSupportedException("AutoHistory only support Deleted and Modified entity.");
            }

            return history;
        }

        private static string PrimaryKey(this EntityEntry entry)
        {
            var key = entry.Metadata.FindPrimaryKey();

            var values = new List<object>();
            foreach (var property in key.Properties)
            {
                var value = entry.Property(property.Name).CurrentValue;
                if (value != null)
                {
                    values.Add(value);
                }
            }

            return string.Join(",", values);
        }
    }
}
