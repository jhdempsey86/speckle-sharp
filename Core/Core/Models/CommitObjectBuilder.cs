﻿#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using Speckle.Core.Logging;
using Speckle.Core.Models.Extensions;

namespace Speckle.Core.Models;

/// <summary>
/// Abstract Builder class for a root commit <see cref="Base"/> object.
/// </summary>
/// <typeparam name="TNativeObjectData">The native object data type needed as input for building <see cref="parentInfos"/></typeparam>
/// <remarks>
/// It is designed to be inherited by a host app specific implementation,
/// to give connectors flexibility in constructing their objects.
/// Inheritors should also create some function to add 
/// </remarks>
/// <example>
/// <see cref=""/>
/// </example>
public abstract class CommitObjectBuilder<TNativeObjectData>
{
  /// <summary>Special appId symbol for the root object</summary>
  protected const string Root = "__Root";
  
  /// <summary>app id -> base</summary>
  protected readonly IDictionary<string, Base> converted;
  
  /// <summary>Base -> Tuple{Parent App Id, propName} ordered by priority</summary>
  private readonly IDictionary<Base, IList<(string? parentAppId, string propName)>> parentInfos;
  
  protected CommitObjectBuilder()
  {
    converted = new Dictionary<string, Base>();
    parentInfos = new Dictionary<Base, IList<(string?,string)>>();
  }

  /// <summary>
  /// Given the parameters, builds connector specific <see cref="parentInfos"/>
  /// to be applied when <see cref="BuildCommitObject"/> is called.
  /// </summary>
  /// <param name="conversionResult"></param>
  /// <param name="nativeElement"></param>
  public abstract void IncludeObject(Base conversionResult, TNativeObjectData nativeElement);
  
  /// <summary>
  /// Iterates through the converted objects applying 
  /// </summary>
  /// <remarks>
  /// Can be overriden to adjust exactly which objects get automatically applied,
  /// or to inject additional items into the <see cref="converted"/> dict that should not be automatically applied.
  /// </remarks>
  /// <param name="rootCommitObject"></param>
  public virtual void BuildCommitObject(Base rootCommitObject)
  {
    ApplyRelationships(converted.Values, rootCommitObject);
  }
  
  /// <summary>
  /// Sets information on how a given object should be nested in the commit tree.
  /// <paramref name="parentInfo"/> encodes the order in which we should try and nest the given <paramref name="conversionResult"/>
  /// when <see cref="CommitObjectBuilder"/> is called
  /// </summary>
  /// <param name="conversionResult">The object to be nested</param>
  /// <param name="parentInfo">Information about how the object ideally should be nested, in order of priority</param>
  protected void SetRelationship(Base conversionResult, params (string? parentAppId, string propName)[] parentInfo)
  {
    if (!converted.ContainsKey(conversionResult.applicationId))
    {
      converted[conversionResult.applicationId] = conversionResult;
    }
    else
    {
      converted.Add(conversionResult.applicationId, conversionResult);
    }

    if (!parentInfos.ContainsKey(conversionResult))
    {
      parentInfos[conversionResult] = parentInfo;
    }
    else
    {
      parentInfos.Add(conversionResult, parentInfo);
    }
  }

  /// <summary>
  /// For each object in <paramref name="ToAdd"/>
  /// <inheritdoc cref="ApplyRelationship"/>
  /// </summary>
  /// <param name="toAdd"></param>
  /// <param name="rootCommitObject"></param>
  protected void ApplyRelationships(IEnumerable<Base> toAdd, Base rootCommitObject)
  {
    foreach (Base c in toAdd)
    {
      try
      {
        ApplyRelationship(c, rootCommitObject);
      }
      catch(Exception ex)
      {
        // This should never happen, we should be ensuring that at least one of the parents is valid.
        SpeckleLog.Logger.Fatal(ex, "Failed to add object {speckleType} to commit object", c?.GetType());
      }
    }
  }
  
  /// <summary>
  /// Will attempt to find and nest the <paramref name="current"/> object
  /// under the first valid parent according to the <see cref="parentInfos"/> <see cref="converted"/> dictionary.
  /// </summary>
  /// <remarks>
  /// A parent is considered valid if
  /// 1. Is non null
  /// 2. Is in the <see cref="converted"/> dictionary
  /// 3. Has (or can dynamically accept) a <see cref="IList"/> typed property with the propName specified by the <see cref="parentInfos"/> item
  /// 4. Said <see cref="IList"/> can accept the <see cref="current"/> object's type 
  /// </remarks>
  /// <param name="current"></param>
  /// <param name="rootCommitObject"></param>
  /// <exception cref="InvalidOperationException">Thrown when no valid parent was found for <see cref="current"/> given <see cref="parentInfos"/></exception>
  protected void ApplyRelationship(Base current, Base rootCommitObject)
  {
    var parents = parentInfos[current];
    foreach ((string? parentAppId, string propName) in parents)
    {
      if (parentAppId is null) continue;

      Base? parent;
      if (parentAppId == Root) parent = rootCommitObject;
      else converted.TryGetValue(parentAppId, out parent);

      if (parent is null)
        continue;

      try
      {
        if (parent.GetDetachedProp(propName) is not IList elements)
        {
          elements = new List<Base>();
          parent.SetDetachedProp(propName, elements);
        }

        elements.Add(current);
        return;
      }
      catch (Exception ex)
      {
        // A parent was found, but it was invalid (Likely because of a type mismatch on a `elements` property)
        SpeckleLog.Logger.Warning(ex, "Failed to add object {speckleType} to a converted parent.", current?.GetType());
      }
    }

    throw new InvalidOperationException($"Could not find a valid parent for object of type {current?.GetType()}. Checked {parents.Count} potential parent, and non were converted!");
  }
}