﻿//
// PROPRIETARY AND CONFIDENTIAL
// Brain Simulator 3 v.1.0
// © 2022 FutureAI, Inc., all rights reserved
// 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BrainSimulator.Modules;

public partial class ModuleUKS : ModuleBase
{
    //This is the actual Universal Knowledge Store
    protected List<Thing> UKSList = new() { Capacity = 1000000, };

    //This is a temporary copy of the UKS which used during the save and restore process to 
    //break circular links by storing index values instead of actual links Note the use of SThing instead of Thing
    public List<SThing> UKSTemp = new();

    //keeps the file name for xml storage
    public string fileName = "";


    //set size parameters as needed in the constructor
    //set max to be -1 if unlimited
    public ModuleUKS()
    {
        allowMultipleDialogs = true;
    }

    public override void Fire()
    {
        Init();  //be sure to leave this here to enable use of the na variable
    }

    //this is needed for the dialog treeview
    public List<Thing> GetTheUKS()
    {
        return UKSList;
    }

    public virtual Thing AddThing(string label, Thing parent, object value = null)
    {
        Thing newThing = new() { V = value };
        if (parent is not null)
        {
            newThing.AddParent(parent);
        }
        lock (UKSList)
        {
            newThing.Label = label;
            UKSList.Add(newThing);
        }
        return newThing;
    }

    public virtual void DeleteThing(Thing t)
    {
        if (t == null) return;
        if (t.Children.Count != 0)
            return; //can't delete something with children...must delete all children first.
        foreach (Relationship r in t.Relationships)
        {
            t.RemoveRelationship(r);
        }
        foreach (Relationship r in t.RelationshipsFrom)
        {
            r.source.RemoveRelationship(r);
        }
        lock (UKSList)
        {
            UKSList.Remove(t);
        }
    }

    //returns the first thing with the given label
    //2nd paramter defines UKS to search, null=search entire knowledge store
    public Thing Labeled(string label)
    {
        Thing retVal = Thing.GetThing(label);
        return retVal;
    }


    public bool ThingInTree(Thing t1, Thing t2)
    {
        if (t2 == null) return false;
        if (t1 == null) return false;
        if (t1 == t2) return true;
        if (t1.AncestorList().Contains(t2)) return true;
        if (t2.AncestorList().Contains(t1)) return true;
        return false;
    }
    bool ThingInTree(Thing t1, string label)
    {
        if (label == "") return false;
        if (t1 == null) return false;
        if (t1.Label == label) return true;
        if (t1.AncestorList().FindFirst(x => x.Label == label) != null) return true;
        if (t1.DescendentsList().FindFirst(x => x.Label == label) != null) return true;
        if (t1.HasRelationshipWithAncestorLabeled(label) != null) return true;
        return false;

    }
    List<Thing> GetTransitiveTargetChain(Thing t, Thing relType, List<Thing> results = null)
    {
        if (results == null) results = new();
        List<Relationship> targets = RelationshipTree(t, relType);
        foreach (Relationship r in targets)
            if (r.reltype == relType)
            {
                if (!results.Contains(r.target))
                {
                    results.Add(r.target);
                    results.AddRange(r.target.Descendents);
                    GetTransitiveTargetChain(r.target, r.reltype, results);
                }
            }
        return results;
    }
    List<Relationship> RelationshipTree(Thing t, Thing relType)
    {
        List<Relationship> results = new();
        results.AddRange(t.Relationships.FindAll(x => x.reltype == relType));
        foreach (Thing t1 in t.Ancestors)
            results.AddRange(t1.Relationships.FindAll(x => x.reltype == relType));
        foreach (Thing t1 in t.Descendents)
            results.AddRange(t1.Relationships.FindAll(x => x.reltype == relType));
        return results;
    }
    List<Thing> GetTransitiveSourceChain(Thing t, Thing relType, List<Thing> results = null)
    {
        if (results == null) results = new();
        List<Relationship> targets = RelationshipsByTree(t, relType);
        foreach (Relationship r in targets)
            if (r.reltype == relType)
            {
                if (!results.Contains(r.source))
                {
                    results.Add(r.source);
                    //results.AddRange(r.source.Ancestors);
                    GetTransitiveSourceChain(r.source, r.reltype, results);
                }
            }
        return results;
    }
    List<Relationship> RelationshipsByTree(Thing t, Thing relType)
    {
        List<Relationship> results = new();
        if (t == null) return results;
        results.AddRange(t.RelationshipsFrom.FindAll(x => x.reltype == relType));
        foreach (Thing t1 in t.Ancestors)
            results.AddRange(t1.RelationshipsFrom.FindAll(x => x.reltype == relType));
        foreach (Thing t1 in t.Descendents)
            results.AddRange(t1.RelationshipsFrom.FindAll(x => x.reltype == relType));
        return results;
    }


    private List<Relationship> HandleTransitivieRelatinships(QueryRelationship searchMask, Thing invType, List<Relationship> queryResult)
    {
        //this is a special case because the relType might be an invers
        if (searchMask.relType == null)
        {
            List<Thing> typesToSearch = new();
            foreach (Relationship r in searchMask.source.Relationships)
            {
                if (HasProperty(r.reltype, "transitive") && !typesToSearch.Contains(r.reltype))
                    typesToSearch.Add(r.reltype);
            }
            foreach (Relationship r in searchMask.source.RelationshipsFrom)
            {
                if (HasProperty(r.reltype, "transitive") && !typesToSearch.Contains(r.reltype))
                    typesToSearch.Add(r.reltype);
            }
            foreach (Relationship r in searchMask.target.Relationships)
            {
                if (HasProperty(r.reltype, "transitive") && !typesToSearch.Contains(r.reltype))
                    typesToSearch.Add(r.reltype);
            }
            foreach (Relationship r in searchMask.target.RelationshipsFrom)
            {
                if (HasProperty(r.reltype, "transitive") && !typesToSearch.Contains(r.reltype))
                    typesToSearch.Add(r.reltype);
            }
            //we are guaranteed that both the source and the target are non-null
            foreach (Thing relType in typesToSearch)
            {
                var forward = GetTransitiveTargetChain(searchMask.source, relType);
                if (forward.Contains(searchMask.target))
                {
                    queryResult.Clear();
                    Relationship newR = new Relationship { source = searchMask.source, T = searchMask.T, reltype = relType };
                    if (!ContainsRelationshipValue(queryResult, newR))
                        queryResult.Add(newR);
                }
                var reverse = GetTransitiveSourceChain(searchMask.source, relType);
                if (reverse.Contains(searchMask.target))
                {
                    //get the inverse relationship
                    invType = CheckForInverse(relType);
                    if (invType != null)
                    {
                        queryResult.Clear();
                        Relationship newR = new Relationship { source = searchMask.source, T = searchMask.T, reltype = invType };
                        if (!ContainsRelationshipValue(queryResult, newR))
                            queryResult.Add(newR);
                    }
                }
            }
            return queryResult; ;
        }

        for (int i = 0; i < queryResult.Count; i++)
        {
            Relationship r = queryResult[i];
            if (!HasProperty(r.reltype, "transitive")) continue;
            if (searchMask.source != null)
            {
                //if the source is defined, get transitive relationships going forward.
                var forward = GetTransitiveTargetChain(r.target, r.relType);
                forward.Remove(r.source);
                if (searchMask.target != null)
                {
                    queryResult.RemoveAt(i);
                    if (forward.Contains(searchMask.target))
                    {
                        Relationship newR = new Relationship { source = r.source, T = searchMask.T, reltype = r.reltype };
                        if (!ContainsRelationshipValue(queryResult, newR))
                            queryResult.Add(newR);
                    }
                }
                else
                {
                    foreach (Thing t in forward)
                    {
                        Relationship newR = new Relationship { source = r.source, T = t, reltype = r.reltype };
                        if (!ContainsRelationshipValue(queryResult, newR))
                            queryResult.Add(newR);
                    }
                    break;
                }
            }
            else if (searchMask.target != null)
            {
                //if the is defined, get transitive relationships going backward.
                var reverse = GetTransitiveSourceChain(r.source, r.relType);
                reverse.Remove(r.target);
                if (searchMask.source != null)
                {
                    queryResult.RemoveAt(i);
                    if (reverse.Contains(searchMask.source))
                    {
                        Relationship newR = new Relationship { source = r.source, T = searchMask.T, reltype = r.reltype };
                        if (!ContainsRelationshipValue(queryResult, newR))
                            queryResult.Add(newR);
                    }
                }
                else
                {
                    foreach (Thing t in reverse)
                    {
                        Relationship newR = new Relationship { source = t, T = searchMask.T, reltype = r.reltype };
                        if (!ContainsRelationshipValue(queryResult, newR))
                            queryResult.Add(newR);
                    }
                    break;
                }
            }
        }
        //if the relationship has an inverse, do the inversion
        if (invType != null)
        {
            for (int i = 0; i < queryResult.Count; i++)
            {
                Relationship r = queryResult[i];
                Relationship newR = new Relationship { source = r.T, T = r.source, reltype = invType };
                queryResult.RemoveAt(i);
                queryResult.Insert(i, newR);
            }
        }

        return queryResult;
    }

    bool ContainsRelationshipValue(List<Relationship> rl, Relationship newR)
    {
        foreach (Relationship r in rl)
        {
            if (r.source == newR.source && r.reltype == newR.reltype && r.target == newR.target) return true;
        }
        return false;
    }

    public List<Relationship> GetAllRelationships(Thing t, int depth, List<Relationship> currentList = null)
    {
        if (currentList == null) currentList = new();
        if (t == null) return currentList;
        foreach (Relationship r in t.Relationships)
            if (!currentList.Contains(r)) currentList.Add(r);
        foreach (Relationship r in t.RelationshipsFrom)
            if (!currentList.Contains(r)) currentList.Add(r);
        if (depth > 0)
        {
            depth--;
            foreach (Relationship r in t.Relationships)
            {
                GetAllRelationships(r.source, depth, currentList);
                GetAllRelationships(r.target, depth, currentList);
            }
            foreach (Relationship r in t.RelationshipsFrom)
            {
                GetAllRelationships(r.source, depth, currentList);
                GetAllRelationships(r.target, depth, currentList);
            }
        }
        return currentList;
    }

    public bool RelationshipsAreExclusive(Relationship r1, Relationship r2)
    {
        //are two relationships mutually exclusive?
        //yes if they differ by a single component property
        //   which is exclusive on a property
        //      which source and target are the ancestor of one another

        //TODO:  expand this to handle
        //  is lessthan is greaterthan
        //  several other cases

        if (r1.target != r2.target && (r1.target == null || r2.target == null)) return false;

        //return false;
        if (r1.source == r2.source ||
            r1.source.AncestorList().Contains(r2.source) ||
            r2.source.AncestorList().Contains(r1.source) ||
            FindCommonParents(r1.source, r1.source).Count() > 0)
        {

            IList<Thing> r1RelProps = GetAttributes(r1.reltype);
            IList<Thing> r2RelProps = GetAttributes(r2.reltype);
            //handle case with properties of the target
            if (r1.target != null && r1.target == r2.target &&
                (r1.target.AncestorList().Contains(r2.target) ||
                r2.target.AncestorList().Contains(r1.target) ||
                FindCommonParents(r1.target, r1.target).Count() > 0))
            {
                IList<Thing> r1TargetProps = GetAttributes(r1.target);
                IList<Thing> r2TargetProps = GetAttributes(r2.target);
                foreach (Thing t1 in r1TargetProps)
                    foreach (Thing t2 in r2TargetProps)
                    {
                        List<Thing> commonParents = FindCommonParents(t1, t2);
                        foreach (Thing t3 in commonParents)
                        {
                            if (HasDirectProperty(t3, "isexclusive") || HasDirectProperty(t3, "allowMultiple"))
                                return true;
                        }
                    }
            }
            //handle case with conflicting targets
            if (r1.target != null && r2.target != null)
            {
                List<Thing> commonParents = FindCommonParents(r1.target, r2.target);
                foreach (Thing t3 in commonParents)
                {
                    if (HasDirectProperty(t3, "isexclusive") || HasDirectProperty(t3, "allowMultiple"))
                        return true;
                }
            }
            if (r1.target == r2.target)
            {
                foreach (Thing t1 in r1RelProps)
                    foreach (Thing t2 in r2RelProps)
                    {
                        if (t1 == t2) continue;
                        List<Thing> commonParents = FindCommonParents(t1, t2);
                        foreach (Thing t3 in commonParents)
                        {
                            if (HasDirectProperty(t3, "isexclusive") || HasDirectProperty(t3, "allowMultiple"))
                                return true;
                        }
                    }
            }
            //if source and target are the same and one contains a number, assume that the other contains "1"
            // fido has a leg -> fido has 1 leg
            bool hasNumber1 = (r1RelProps.FindFirst(x => x.HasAncestorLabeled("number")) != null);
            bool hasNumber2 = (r2RelProps.FindFirst(x => x.HasAncestorLabeled("number")) != null);
            if (r1.source == r2.source && r1.target == r2.target &&
                (hasNumber1 || hasNumber2))
                return true;

            //if one of the reltypes contains negation and not the other
            Thing r1Not = r1RelProps.FindFirst(x => x.Label == "not");
            Thing r2Not = r2RelProps.FindFirst(x => x.Label == "not");
            if (r1.source == r2.source && r1.target == r2.target &&
                (r1Not == null && r2Not != null || r1Not != null && r2Not == null))
                return true;
        }
        else
        {
            List<Thing> commonParents = FindCommonParents(r1.target, r2.target);
            foreach (Thing t3 in commonParents)
            {
                if (HasDirectProperty(t3, "isexclusive"))
                    return true;
                if (HasDirectProperty(t3, "allowMultiple") && r1.source != r2.source)
                    return true;
            }

        }
        return false;
    }

    public IList<Thing> GetAttributes(Thing t)
    {
        List<Thing> retVal = new();
        if (t == null) return retVal;
        foreach (Relationship r in t.Relationships)
        {
            if (r.reltype != null && r.reltype.Label == "is")
                retVal.Add(r.target);
        }
        return retVal;
    }
    bool HasProperty(Thing t, string propertyName)
    {
        if (t == null) return false;
        var v = RelationshipWithInheritance(t);
        if (v.FindFirst(x => x.T?.Label.ToLower() == propertyName.ToLower() && x.reltype.Label == "hasProperty") != null) return true;
        return false;
    }
    bool HasDirectProperty(Thing t, string propertyName)
    {
        if (t == null) return false;
        var v = t.Relationships;
        if (v.FindFirst(x => x.T?.Label.ToLower() == propertyName.ToLower() && x.reltype.Label == "hasProperty") != null) return true;
        return false;
    }

    private void AddPropertiesToList(Thing theParent, List<Thing> retVal, IList<Thing> properties)
    {
        foreach (Thing t in properties)
        {
            if (t.HasAncestor(theParent) && !retVal.Contains(t))
                retVal.Add(t);
        }
    }


    bool RelationshipsAreEqual(Relationship r1, Relationship r2, bool ignoreSource = true)
    {
        if (
            (r1.source == r2.source || ignoreSource) &&
            r1.target == r2.target &&
            r1.relType == r2.relType
          //ListsAreEqual(r1.typeProperties, r2.typeProperties) &&
          //ListsAreEqual(r1.targetProperties, r2.targetProperties)
          ) return true;
        return false;
    }


    //every Thing in the first list MUST occur in the 2nd
    bool ModifiersMatch(List<Thing> l1, Thing t)
    {
        foreach (Thing t1 in l1)
        {
            if (t.Relationships.FindFirst(x => x.reltype != null && x.reltype.Label == "is" && x.T == t1) == null)
                return false;
        }
        return true;
    }
    void AddToList(List<Relationship> l1, Relationship r)
    {
        if (r != null && !l1.Contains(r))
            l1.Add(r);
    }


    private List<Thing> ThingListFromStringList(List<string> modifiers, string defaultParent)
    {
        if (modifiers == null) return null;
        List<Thing> retVal = new();
        foreach (string s in modifiers)
        {
            Thing t = ThingFromString(s, defaultParent);
            if (t != null) retVal.Add(t);
        }
        return retVal;
    }

    private Thing ThingFromString(string s, string defaultParent)
    {
        if (string.IsNullOrEmpty(s)) return null;
        Thing t = Labeled(s);

        if (t == null)
        {
            if (Labeled(defaultParent) == null)
            {
                GetOrAddThing(defaultParent, Labeled("Object"));
            }
            t = GetOrAddThing(s, defaultParent);
        }
        return t;
    }

    //temporarily public for testing
    public Thing ThingFromObject(object o, string parentLabel = "")
    {
        if (parentLabel == "")
            parentLabel = "unknownObject";
        if (o is string s3)
            return ThingFromString(s3.Trim(), parentLabel);
        else if (o is Thing t3)
            return t3;
        else if (o is null)
            return null;
        else
        {
            return null;
        }
    }
    private List<Thing> ThingListFromString(string s, string parentLabel = "unknownObject")
    {
        List<Thing> retVal = new List<Thing>();
        string[] multiString = s.Split("|");
        foreach (string s1 in multiString)
        {
            Thing t = ThingFromString(s1.Trim(), parentLabel);
            if (t != null)
                retVal.Add(t);
        }
        return retVal;
    }
    //temporarily public for testing
    public List<Thing> ThingListFromObject(object o, string parentLabel = "unknownObject")
    {
        if (o is List<string> sl)
            return ThingListFromStringList(sl, parentLabel);
        else if (o is string s)
            return ThingListFromString(s, parentLabel);
        else if (o is Thing t)
            return new() { t };
        else if (o is List<Thing> tl)
            return tl;
        else if (o is null)
            return new();
        else
            throw new ArgumentException("invalid arg type in AddStatement: " + o.GetType());
    }



    //returns the first thing it encounters which with a given value or null if none is found
    //the 2nd paramter defines the UKS to search (e.g. list of children)
    //if it is null, it searches the entire UKS,
    //the 3rd paramter defines the tolerance for spatial matches
    //if it is null, an exact match is required
    public virtual Thing Valued(object value, IList<Thing> UKSt = null, float toler = 0, string label = "")
    {
        UKSt ??= UKSList;
        if (value is null) return null;
        lock (UKSt)
        {
            foreach (Thing t in UKSt)
            {
                if (label != "" && t.Label != label) continue;
                //if (t is null) continue; //this should never happen
                if (t.V is PointPlus p1 && value is PointPlus p2)
                {
                    if (p1.Near(p2, toler))
                    {
                        t.useCount++;
                        return t;
                    }
                }
                else
                {
                    if (t.V is not null && t.V.ToString().ToLower().Trim() == value.ToString().ToLower().Trim())
                    {
                        t.useCount++;
                        return t;
                    }
                }
            }
            return null;
        }
    }

    //TODO ?move to Thing  t.DeleteAllChildren()
    public void DeleteAllChildren(Thing t)
    {
        if (t is not null)
        {
            while (t.Children.Count > 0)
            {
                IList<Thing> children = t.Children;
                if (t.Children[0].Parents.Count == 1)
                {
                    DeleteAllChildren(children[0]);
                    DeleteThing(children[0]);
                }
                else
                {//this thing has multiple parents.
                    t.RemoveChild(children[0]);
                }
            }
        }

    }

    //This buffers the location of the unknownObject tree for consistency and speed
    private Thing unknown;
    public Thing Unknown
    {
        get
        {
            if (unknown == null)
                unknown = Labeled("unknownObject");
            return unknown;
        }
    }

    // If a thing exists, return it.  If not, create it.
    // If it is currently an unknown, defining the parent can make it known
    // A value can optionally be defined.
    public Thing GetOrAddThing(string label, object parent = null, object value = null, bool reuseValue = false)
    {
        Thing thingToReturn = null;

        if (string.IsNullOrEmpty(label)) return thingToReturn;

        thingToReturn = Thing.GetThing(label);
        if (thingToReturn != null) return thingToReturn;

        Thing correctParent = null;
        if (parent is string s)
            correctParent = Thing.GetThing(s);
        if (parent is Thing t)
            correctParent = t;
        if (correctParent == null)
            correctParent = Thing.GetThing("unknownObject");

        if (correctParent is null) throw new ArgumentException("GetOrAddThing: could not find parent");

        thingToReturn = AddThing(label, correctParent);

        //thingToReturn = GetThingWithCorrectParent(label, parent);

        //if (thingToReturn == null)
        //{
        //    = GetCorrectParent(label, parent);

        //    if (correctParent != null)
        //    {
        //        thingToReturn = GetLabeledOrValuedThing(label, correctParent, value, reuseValue);
        //    }
        //    if (thingToReturn == null)
        //    {
        //        thingToReturn = GetUnknownParentToBeKnown(label, correctParent);

        //        if (thingToReturn == null)
        //        {
        //            thingToReturn = AddNewThingToCorrectParent(label, thingToReturn, correctParent);
        //        }
        //    }
        //}

        //if (thingToReturn != null && value != null)
        //{
        //    thingToReturn.V = value;
        //}

        return thingToReturn;
    }

    private Thing GetThingWithCorrectParent(string label, object parent)
    {
        if (parent is null)
            return Labeled(label);
        return (Thing)null;
    }

    private Thing GetCorrectParent(string label, object parent)
    {
        Thing correctParent = null;

        if (parent is Thing t)
        {
            correctParent = t;
        }
        else if (parent is string s1 && s1 != "")
        {
            correctParent = Labeled(s1);
            if (correctParent == null)
            {
                correctParent = AddThing(s1, Unknown);
            }
        }
        else if (parent is null)
        {
            correctParent = Unknown;
        }
        return correctParent;
    }

    private Thing GetLabeledOrValuedThing(string label, Thing correctParent, object value, bool reuseValue)
    {
        Thing thingToReturn = Labeled(label);
        if (value is not null && (value is string || reuseValue))
        {
            thingToReturn = Valued(value, correctParent.Children, 0, label);
            thingToReturn ??= AddThing(label, correctParent, value);
        }
        return thingToReturn;
    }

    private Thing GetUnknownParentToBeKnown(string label, Thing correctParent)
    {
        Thing thingToReturn = null;
        if (Unknown != null)
        {
            thingToReturn = Labeled(label);
        }
        if (thingToReturn != null && correctParent != null)
        {
            thingToReturn.AddParent(correctParent);
            thingToReturn.RemoveParent(unknown);
        }
        return thingToReturn;
    }

    private Thing AddNewThingToCorrectParent(string label, Thing thingToReturn, Thing correctParent)
    {
        if (correctParent != null)
        {
            thingToReturn = AddThing(label, correctParent);
        }
        if (thingToReturn == null)
        {
            correctParent = unknown;
            thingToReturn = AddThing(label, correctParent);
        }
        return thingToReturn;
    }

    public Thing SetParent(object child, object parent)
    {
        Thing tParent, tChild;
        if (parent is string sParent)
        {
            tParent = Labeled(sParent);
            if (tParent == null)
                tParent = GetOrAddThing(sParent, Unknown);
        }
        else if (parent is Thing t) tParent = t;
        else throw new ArgumentException("Invalid Type");

        if (child is string sChild)
        {
            tChild = Labeled(sChild);
            if (tChild == null)
                tChild = GetOrAddThing(sChild, Unknown);
        }
        else if (child is Thing t) tChild = t;
        else throw new ArgumentException("Invalid Type");

        return SetParent(tChild, tParent);
    }

    public Thing SetParent(Thing child, Thing parent)
    {
        if (parent.Children.Contains(child))
        {
            //TODO increase confidence in relationship
            return parent;
        }
        parent.AddChild(child);
        Thing uk1 = GetOrAddThing("unknownObject", "Object");
        Thing uk2 = GetOrAddThing("unknownModifier", "Object");
        child.RemoveParent(uk1);
        child.RemoveParent(uk2);

        return parent;
    }

    public void SetupNumbers()
    {
        GetOrAddThing("is-a", "RelationshipType");
        GetOrAddThing("inverseOf", "RelationshipType");
        GetOrAddThing("hasProperty", "RelationshipType");
        GetOrAddThing("is", "RelationshipType");

        AddStatement("is-a", "inverseOf", "has-child");
        AddStatement("isexclusive", "is-a", "RelationshipType");
        AddStatement("with", "is-a", "RelationshipType");
        AddStatement("and", "is-a", "Relationship");
        AddStatement("isSimilarTo", "is-a", "RelationshipType");
        AddStatement("greaterThan", "is-a", "RelationshipType");


        //put in numbers 1-4
        GetOrAddThing("zero", "number").V = (int)0;
        GetOrAddThing("one", "number").V = (int)1;
        GetOrAddThing("two", "number").V = (int)2;
        GetOrAddThing("three", "number").V = (int)3;
        GetOrAddThing("four", "number").V = (int)4;
        GetOrAddThing("ten", "number").V = (int)10;
        Thing some = GetOrAddThing("some", "number");
        Thing many = GetOrAddThing("many", "number");
        Thing none = GetOrAddThing("none", "number");
        SetParent(Labeled("number"), "Object");
        none.V = (int)0;

        AddStatement("10", "isSimilarTo", "ten");
        AddStatement("10", "is-a", "number");
        AddStatement("4", "isSimilarTo", "four");
        AddStatement("4", "is-a", "number");
        AddStatement("3", "isSimilarTo", "three");
        AddStatement("3", "is-a", "number");
        AddStatement("2", "isSimilarTo", "two");
        AddStatement("2", "is-a", "number");
        AddStatement("1", "isSimilarTo", "one");
        AddStatement("1", "is-a", "number");
        AddStatement("0", "isSimilarTo", "zero");
        AddStatement("0", "is-a", "number");

        AddStatement("one", "greaterThan", "none");
        AddStatement("two", "greaterThan", "one");
        AddStatement("three", "greaterThan", "two");
        AddStatement("four", "greaterThan", "three");
        AddStatement("many", "greaterThan", "four");
        AddStatement("many", "greaterThan", "some");
        AddStatement("some", "greaterThan", "none");
        AddStatement("number", "hasProperty", "isexclusive");
    }

    public void SetupPronouns()
    {
        List<Thing> cases = new();
        GetOrAddThing("pronoun", "Object");
        GetOrAddThing("pronounType", "pronoun");
        GetOrAddThing("plural", "pronounType");
        GetOrAddThing("singular", "pronounType");
        GetOrAddThing("gender", "pronounType");
        GetOrAddThing("masculine", "gender");
        GetOrAddThing("feminine", "gender");
        GetOrAddThing("personPro", "pronounType");
        GetOrAddThing("1st", "personPro");
        GetOrAddThing("2nd", "personPro");
        GetOrAddThing("3rd", "personPro");
        GetOrAddThing("4th", "personPro"); //demonstrative
        GetOrAddThing("5th", "personPro"); //demonstrative
        //not sure how we'll use these:
        cases.Add(GetOrAddThing("subj", "pronounType"));
        cases.Add(GetOrAddThing("obj", "pronounType"));
        cases.Add(GetOrAddThing("possAdj", "pronounType"));
        cases.Add(GetOrAddThing("poss", "pronounType"));
        cases.Add(GetOrAddThing("refl", "pronounType"));

        //person (assumes singular) subj obje posseive poss adj refl
        //TODO: different languages may require more cases
        List<string> pronouns = new()
        {
            "1 i me my mine myself",
            "2 you you your yours yourself",
            "3m he him his his himself",
            "3f she her her hers herself",
            "3 it it its its itself",
            "1p we us our ours ourselves",
            "2p you you your yours yourselves",
            "3p they them their theirs themselves",
            "4 this this its its itself",  //4 is demonstrative near
            "5 that that its its itself",  //5 is demonstrative far
            "4p these these their theirs themselves",
            "5p those those their theirs themselves",
        };

        foreach (string pronoun in pronouns)
        {
            string[] items = pronoun.Split(' ');
            Debug.Assert(items.Length == 6);

            int.TryParse(items[0][0].ToString(), out int person);
            char modifier = ' ';
            if (items[0].Length > 1)
            {
                modifier = items[0][1];
            }
            for (int i = 1; i < items.Length; i++)
            {
                Thing t = GetOrAddThing(items[i], "pronoun");
                AddStatement(t, "is", cases[i - 1]);
                switch (person)
                {
                    case 1:
                        AddStatement(t, "is", "1st");
                        break;
                    case 2:
                        AddStatement(t, "is", "2nd");
                        break;
                    case 3:
                        AddStatement(t, "is", "3rd");
                        break;
                    case 4:
                        AddStatement(t, "is", "4th");
                        break;
                    case 5:
                        AddStatement(t, "is", "5th");
                        break;
                }
                switch (modifier)
                {
                    case 'p':
                        AddStatement(t, "is", "plural");
                        break;
                    case 'm':
                        AddStatement(t, "is", "masculine");
                        AddStatement(t, "is", "singular");
                        break;
                    case 'f':
                        AddStatement(t, "is", "feminine");
                        AddStatement(t, "is", "singular");
                        break;
                    case ' ':
                        AddStatement(t, "is", "singular");
                        break;
                }
            }
        }
    }
}