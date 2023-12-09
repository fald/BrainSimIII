﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Math;


namespace BrainSimulator.Modules
{
    public partial class ModuleUKS
    {
        /*
        Search for:
         Things by properties
            (by property value)
         If property is numeric or color, tolerance
         Things by relationship
     Intersection of two lists
     Union of two lists
     Order by
         useCount
         distance from visual center
        number of matches

     Harder: landmark match
        */


        public List<Thing> QueryRelationships(Thing baseThing, List<string> relationships, IList<Thing> rangeOfSearch = null)
        {
            if (rangeOfSearch == null)
            {
                Thing mentalModel = GetOrAddThing("MentalModel", "Thing");
                if (mentalModel is null) return null;
                rangeOfSearch = mentalModel.Children;
            }

            Thing relationshipRoot = GetOrAddThing("RelationshipType", "Thing");
            List<Thing> retVal = new();

            //bulld list of relationships
            List<Thing> relationshipThings = new();
            IList<Thing> allRelationships = relationshipRoot.DescendentsList();

            foreach (var relationship in relationships)
            {
                Thing t = Labeled(relationship);
                if (t != null) relationshipThings.Add(t);
            }
            //default temp to first object referenced by base object
            foreach (var l in baseThing.Relationships)
            {
                if (l.relType is not null)
                {
                    foreach (Thing relation in relationshipThings)
                    {
                        if (l.relType == relation)
                        {
                            retVal.Add(l.T as Thing);
                        }
                    }
                }
            }
            return retVal;
        }

        public IList<Thing> QueryByUseCount(IList<Thing> rangeOfSearch = null)
        {
            if (rangeOfSearch == null)
            {
                Thing mentalModel = GetOrAddThing("MentalModel", "Thing");
                if (mentalModel is null) return null;
                rangeOfSearch = mentalModel.Children;
            }
            return rangeOfSearch.OrderByDescending(x => x.useCount).ToList();
        }

        private class Counts
        {
            public Thing t; public int count = 1;
        }
        
        public IList<Thing> QueryPropertiesByAssociatedWords(List<Thing> words, bool exactMatch = false)
        {
            List<Counts> theCounts = new();
            //Thing propertyRoot = GetOrAddThing("Property", "Thing");
            List<Thing> propertyThings = new();

            foreach (var word in words)
            {
                float bestValue = float.MinValue;
                int bestMisses = int.MaxValue;
                Thing bestMatch = null;
                foreach ( Relationship l in word.Relationships)
                {
                    if (((l.T as Thing).HasAncestor(Labeled("col")) || ((l.T as Thing).HasAncestor(Labeled("shp")))) && l.Value >= bestValue)
                    {
                        if ( l.Value > bestValue || l.misses < bestMisses )
                        {
                            bestMisses = l.misses;
                            bestValue = l.Value;
                            bestMatch = l.T as Thing;
                        }
                    }
                }
                if ( bestMatch != null) propertyThings.Add(bestMatch);
            }
            foreach (Thing prop in propertyThings)
            {
                foreach (Relationship l in prop.RelationshipsFrom)
                {
                    if ((l.T as Thing).HasAncestor(GetOrAddThing("Word", "Audible"))) continue;
                    foreach (Counts c in theCounts)
                    {
                        if (c.t == l.T )
                        {
                            c.count++;
                            goto Found;
                        }
                    }
                    theCounts.Add(new Counts { t = l.T as Thing });
                Found: { }
                }
            }
            theCounts = theCounts.OrderByDescending(x => x.count).ToList();

            IList<Thing> retVal = new List<Thing>();
            foreach (Counts c in theCounts)
            {
                if (exactMatch)
                {
                    if (c.count >= words.Count) retVal.Add(c.t);
                }
                else retVal.Add(c.t);
            }

            return retVal;
        }

        internal Thing QueryPropertiesThroughObjectTree(List<string> properties)
        {
            return QueryPropertiesThroughObjectTreeList(properties)?[0];
        }

        // Searches the UKS 
        // Returns a list with at least one item, or null.
        internal IList<Thing> QueryPropertiesThroughObjectTreeList(List<string> properties)
        {
            if (properties.Count == 0) return null;
            IList<Thing> retval = Labeled(properties[0])?.GetRelationshipByWithAncestor(Labeled("MentalModel")).ConvertAll(l => l.T as Thing);

            if ( properties.Count > 0 )
                for ( int i = 1; i < properties.Count; i++ )
                {
                    retval = retval.FindAll(t => t.GetRelationshipsWithAncestor(Labeled("Object")).Exists(r => (r.T as Thing).Label == properties[i]));
                }

            if (retval == null) return null;
            return retval.Count == 0 ? null:retval;
        }

        
        private bool PropertiesNear(object o1, object o2, float tolerance)
        {
            if (o1 == null || o2 == null) return false;
            if (o1 is Point3DPlus p1 && o2 is Point3DPlus p2)
            {
                if (Math.Abs(p1.Theta - p2.Theta) < Angle.FromDegrees(tolerance) &&
                    Math.Abs(p1.Phi - p2.Phi) < Angle.FromDegrees(tolerance))
                    return true;
            }
            else if (o1 is HSLColor c1 && o2 is HSLColor c2)
            {
                if (Math.Abs(c1.hue - c2.hue) < tolerance)
                    return true;
            }
            else if (o1 is Thing t1 && o2 is string s)
            {
                if (t1.Label == s)
                    return true;
            }
            else if (o1.ToString() == "Partial" || o2.ToString() == "Partial")
                return true;
            else if (o1.ToString() == o2.ToString())
                return true;
            return false;
        }

        //TODO rewiite this!
        public List<Thing> SearchPhysicalObjectOld(Dictionary<string, object> properties, float tolerance = 3, bool visibleOnly = false)
        {
            Thing propertiesRoot = GetOrAddThing("Property", "Thing");
            Thing transientPropertiesRoot = GetOrAddThing("TransientProperty", "Property");
            List<Thing> tList = new List<Thing>();
            if (properties.Count == 0) return tList;

            foreach (KeyValuePair<string, object> prop in properties)
            {
                string propName = prop.Key;
                Thing propRoot = Labeled(propName);
                if (propRoot == null)
                    propRoot = Labeled(propName);
                if (propRoot != null)
                {
                    // one or more matching propert types found...
                    foreach (Thing t in propRoot.Children)
                    {
                        // are they considered "near" matches?
                        if (prop.Value == t)
                        {
                            foreach (Relationship l in t.RelationshipsFrom)
                            {
                                tList.Add((l.T as Thing));
                            }
                        }
                        else if (PropertiesNear(prop.Value, t.V, tolerance))
                        {
                            // if so add all their references to the list.
                            foreach (Relationship l in t.RelationshipsFrom)
                            {
                                tList.Add((l.T as Thing));
                            }
                        }
                    }
                }
            }
            return tList;
        }

        public override void UKSInitializedNotification()
        {
            MainWindow.SuspendEngine();
            MainWindow.ResumeEngine();
        }
    }
}
