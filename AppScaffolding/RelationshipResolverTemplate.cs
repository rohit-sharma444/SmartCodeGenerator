namespace AppScaffolding;

/// <summary>
/// Emits the generated app's FK -> navigation-property resolver.
/// </summary>
/// <remarks>
/// The nav properties DomainModelMapper emits are [JsonIgnore], so they are never persisted — only
/// the foreign keys are, which is what keeps Order.Customer -> Customer.Orders -> Order from being
/// a cycle System.Text.Json throws on. That leaves the nav properties null at load, and this is
/// what fills them in.
/// <para>
/// Reflection-driven on purpose: one file, no per-entity code, and it works for any model the
/// discovery pipeline produces.
/// </para>
/// </remarks>
public static class RelationshipResolverTemplate
{
    /// <summary>Source of <c>Data/RelationshipResolver.cs</c> for <paramref name="appName"/>.</summary>
    public static string Source(string appName) => $$"""
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using {{appName}}.ViewModels;

namespace {{appName}}.Data;

/// <summary>
/// Fills in the navigation properties from the foreign keys, once every store has loaded.
/// </summary>
/// <remarks>
/// The JSON files hold only foreign keys (the nav properties are [JsonIgnore], or saving would
/// hit a reference cycle). So after load, Order.CustomerId is set and Order.Customer is null.
/// This walks every loaded record and joins the two back up.
/// </remarks>
public static class RelationshipResolver
{
    public static void Resolve(IEnumerable<IEntityViewModel> tabs)
    {
        var records = tabs.SelectMany(tab => tab.Items.Cast<object>()).ToList();

        // Type -> Id -> record. Every lookup below is a hit in this map.
        var byTypeAndId = new Dictionary<Type, Dictionary<int, object>>();

        foreach (var record in records)
        {
            var id = IdOf(record);
            if (id is null)
                continue;

            if (!byTypeAndId.TryGetValue(record.GetType(), out var byId))
                byTypeAndId[record.GetType()] = byId = new Dictionary<int, object>();

            byId[id.Value] = record;
        }

        foreach (var record in records)
        {
            ResolveReferences(record, byTypeAndId);
            ResolveCollections(record, byTypeAndId);
        }
    }

    /// <summary>Sets Order.Customer from Order.CustomerId.</summary>
    private static void ResolveReferences(
        object record, Dictionary<Type, Dictionary<int, object>> byTypeAndId)
    {
        foreach (var property in record.GetType().GetProperties())
        {
            if (!byTypeAndId.TryGetValue(property.PropertyType, out var byId))
                continue;

            var key = record.GetType().GetProperty(property.Name + "Id");
            if (key is null || key.PropertyType != typeof(int))
                continue;

            if (key.GetValue(record) is int id && byId.TryGetValue(id, out var target))
                property.SetValue(record, target);
        }
    }

    /// <summary>Sets Customer.Orders to every Order whose CustomerId is this customer's Id.</summary>
    private static void ResolveCollections(
        object owner, Dictionary<Type, Dictionary<int, object>> byTypeAndId)
    {
        var ownerId = IdOf(owner);
        if (ownerId is null)
            return;

        foreach (var property in owner.GetType().GetProperties())
        {
            if (!property.PropertyType.IsGenericType
                || property.PropertyType.GetGenericTypeDefinition() != typeof(List<>))
                continue;

            var itemType = property.PropertyType.GetGenericArguments()[0];
            if (!byTypeAndId.TryGetValue(itemType, out var candidates))
                continue;

            var foreignKey = itemType.GetProperty(owner.GetType().Name + "Id");
            if (foreignKey is null || foreignKey.PropertyType != typeof(int))
                continue;

            var list = (IList)Activator.CreateInstance(property.PropertyType)!;

            foreach (var candidate in candidates.Values)
            {
                if (foreignKey.GetValue(candidate) is int id && id == ownerId.Value)
                    list.Add(candidate);
            }

            property.SetValue(owner, list);
        }
    }

    private static int? IdOf(object record)
        => record.GetType().GetProperty("Id",
               BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
               ?.GetValue(record) as int?;
}
""";
}
