﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Speckle.ConnectorRevit.Storage;
using Speckle.Core.Api;
using Speckle.Core.Kits;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Transports;
using Speckle.DesktopUI.Utils;
using Stylet;
using RevitElement = Autodesk.Revit.DB.Element;

namespace Speckle.ConnectorRevit.UI
{
  public partial class ConnectorBindingsRevit
  {


    //TODO: store these string values in something more solid to avoid typos?
    public override List<ISelectionFilter> GetSelectionFilters()
    {
      var categories = new List<string>();
      var parameters = new List<string>();
      var views = new List<string>();
      var projectInfo = new List<string> { "Project Info", "Levels", "Views 2D", "Views 3D", "Families & Types" };

      if (CurrentDoc != null)
      {
        //selectionCount = CurrentDoc.Selection.GetElementIds().Count();
        categories = ConnectorRevitUtils.GetCategoryNames(CurrentDoc.Document);
        parameters = ConnectorRevitUtils.GetParameterNames(CurrentDoc.Document);
        views = ConnectorRevitUtils.GetViewNames(CurrentDoc.Document);
      }

      return new List<ISelectionFilter>
      {
        new ListSelectionFilter {Name = "Category", Icon = "Category", Values = categories, Description="Adds all objects belonging to the selected categories"},
        new ListSelectionFilter {Name = "View", Icon = "RemoveRedEye", Values = views, Description="Adds all objects visible in the selected views" },
        new ListSelectionFilter {Name = "Project Info", Icon = "Information", Values = projectInfo, Description="Adds the selected project information such as levels, views and family names to the stream"},
        new PropertySelectionFilter
        {
          Name = "Parameter",
          Description="Adds  all objects satisfying the selected parameter",
          Icon = "FilterList",
          HasCustomProperty = false,
          Values = parameters,
          Operators = new List<string> {"equals", "contains", "is greater than", "is less than"}
        }
      };
    }

    public override List<string> GetSelectedObjects()
    {
      if (CurrentDoc == null)
      {
        return new List<string>();
      }

      var selectedObjects = CurrentDoc.Selection.GetElementIds().Select(id => CurrentDoc.Document.GetElement(id).UniqueId).ToList();
      return selectedObjects;
    }

    public override List<string> GetObjectsInView()
    {
      if (CurrentDoc == null)
      {
        return new List<string>();
      }

      var collector = new FilteredElementCollector(CurrentDoc.Document, CurrentDoc.Document.ActiveView.Id).WhereElementIsNotElementType();
      var elementIds = collector.ToElements().Select(el => el.UniqueId).ToList(); ;

      return elementIds;
    }

    /// <summary>
    /// Given the filter in use by a stream returns the document elements that match it.
    /// </summary>
    /// <param name="filter"></param>
    /// <returns></returns>
    private List<Element> GetSelectionFilterObjects(ISelectionFilter filter)
    {
      var doc = CurrentDoc.Document;

      var selection = new List<Element>();

      switch (filter.Name)
      {
        case "Category":
          var catFilter = filter as ListSelectionFilter;
          var bics = new List<BuiltInCategory>();
          var categories = ConnectorRevitUtils.GetCategories(doc);
          IList<ElementFilter> elementFilters = new List<ElementFilter>();

          foreach (var cat in catFilter.Selection)
          {
            elementFilters.Add(new ElementCategoryFilter(categories[cat].Id));
          }

          var categoryFilter = new LogicalOrFilter(elementFilters);

          selection = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .WhereElementIsViewIndependent()
            .WherePasses(categoryFilter).ToList();
          return selection;

        case "View":
          var viewFilter = filter as ListSelectionFilter;

          var views = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfClass(typeof(View))
            .Where(x => viewFilter.Selection.Contains(x.Name));

          foreach (var view in views)
          {
            var ids = selection.Select(x => x.UniqueId);

            var viewElements = new FilteredElementCollector(doc, view.Id)
              .WhereElementIsNotElementType()
              .WhereElementIsViewIndependent()
              .Where(x => x.IsPhysicalElement())
              .Where(x => !ids.Contains(x.UniqueId)) //exclude elements already added from other views
              .ToList();

            selection.AddRange(viewElements);
          }
          return selection;

        case "Project Info":
          var projectInfoFilter = filter as ListSelectionFilter;

          if (projectInfoFilter.Selection.Contains("Project Info"))
          {
            selection.Add(doc.ProjectInformation);
          }

          if (projectInfoFilter.Selection.Contains("Views 2D"))
          {
            selection.AddRange(new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfCategory(BuiltInCategory.OST_Views)
            .Cast<View>()
            .Where(x => x.ViewType == ViewType.CeilingPlan ||
            x.ViewType == ViewType.FloorPlan ||
            x.ViewType == ViewType.Elevation ||
            x.ViewType == ViewType.Section)
            .ToList());
          }

          if (projectInfoFilter.Selection.Contains("Views 3D"))
          {
            selection.AddRange(new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfCategory(BuiltInCategory.OST_Views)
            .Cast<View>()
            .Where(x => x.ViewType == ViewType.ThreeD)
            .ToList());
          }

          if (projectInfoFilter.Selection.Contains("Levels"))
          {
            selection.AddRange(new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .OfCategory(BuiltInCategory.OST_Levels).ToList());
          }

          if (projectInfoFilter.Selection.Contains("Families & Types"))
          {
            //get all the elementtypes of the categories we support
            var allCategoryFilter = new LogicalOrFilter(ConnectorRevitUtils.GetCategories(doc).Select(x => new ElementCategoryFilter(x.Value.Id)).Cast<ElementFilter>().ToList());

            selection.AddRange(new FilteredElementCollector(doc)
            .WhereElementIsElementType()
            .WherePasses(allCategoryFilter).ToList());
          }
          return selection;

        case "Parameter":
          try
          {
            var propFilter = filter as PropertySelectionFilter;
            var query = new FilteredElementCollector(doc)
              .WhereElementIsNotElementType()
              .WhereElementIsNotElementType()
              .WhereElementIsViewIndependent()
              .Where(x => x.IsPhysicalElement())
              .Where(fi => fi.LookupParameter(propFilter.PropertyName) != null);

            propFilter.PropertyValue = propFilter.PropertyValue.ToLowerInvariant();

            switch (propFilter.PropertyOperator)
            {
              case "equals":
                query = query.Where(fi =>
                  GetStringValue(fi.LookupParameter(propFilter.PropertyName)) == propFilter.PropertyValue);
                break;
              case "contains":
                query = query.Where(fi =>
                  GetStringValue(fi.LookupParameter(propFilter.PropertyName)).Contains(propFilter.PropertyValue));
                break;
              case "is greater than":
                query = query.Where(fi => UnitUtils.ConvertFromInternalUnits(
                                            fi.LookupParameter(propFilter.PropertyName).AsDouble(),
                                            fi.LookupParameter(propFilter.PropertyName).DisplayUnitType) >
                                          double.Parse(propFilter.PropertyValue));
                break;
              case "is less than":
                query = query.Where(fi => UnitUtils.ConvertFromInternalUnits(
                                            fi.LookupParameter(propFilter.PropertyName).AsDouble(),
                                            fi.LookupParameter(propFilter.PropertyName).DisplayUnitType) <
                                          double.Parse(propFilter.PropertyValue));
                break;
            }

            selection = query.ToList();
          }
          catch (Exception e)
          {
            Log.CaptureException(e);
          }
          return selection;
      }

      return selection;

    }

    private string GetStringValue(Parameter p)
    {
      string value = "";
      if (!p.HasValue)
      {
        return value;
      }

      if (string.IsNullOrEmpty(p.AsValueString()) && string.IsNullOrEmpty(p.AsString()))
      {
        return value;
      }

      if (!string.IsNullOrEmpty(p.AsValueString()))
      {
        return p.AsValueString().ToLowerInvariant();
      }
      else
      {
        return p.AsString().ToLowerInvariant();
      }
    }
  }
}