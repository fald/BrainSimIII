﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace UKS;
public partial class UKS
{
    List<Relationship> failedConditions = new();
    List<Relationship> succeededConditions = new();

    /// <summary>
    /// Gets all relationships to a gropu of Things includeing inherited relationships
    /// </summary>
    /// <param name="sources"></param>
    /// <param name="reverse">if true, the first parameter is a list of targets rather than sources</param>
    /// <returns>List of matching relationships</returns>
    public List<Thing> GetAllAttributes(Thing t) //with inheritance, conflicts, etc
    {
        //TODO This is hard-codedd to "is" relationships
        List<Thing> retVal = new();

        var temp1 = BuildSearchList(new List<Thing>() { t });
        var temp2 = GetAllRelationshipsInt(temp1);
        foreach (Relationship r in temp2)
            if (r.relType.Label == "is")
                retVal.Add(r.target);
        return retVal;
    }
    public List<Relationship> GetAllRelationships(List<Thing> sources, bool reverse) //with inheritance, conflicts, etc
    {
        var result1 = BuildSearchList(sources, reverse);
        List<Relationship> result2 = GetAllRelationshipsInt(result1);
        RemoveConflictingResults(result2);
        RemoveFalseConditionals(result2);
        AlphabetizeRelationships(ref result2);
        return result2;
    }

    private void AlphabetizeRelationships(ref List<Relationship> result2)
    {
        result2 = result2.OrderBy(x => x.ToString()).ToList();
    }


    //This is used to store temporary content during queries
    private class ThingWithQueryParams
    {
        public Thing thing;
        public int hopCount;
        public int haveCount = 1;
        public int hitCount = 1;
        public float weight;
        public Thing reachedWith = null;
        public bool corner = false;
        public override string ToString()
        {
            return (thing.Label + "  : " + hopCount + " : " + weight + "  Count: " +
                haveCount + " Hits: " + hitCount + " Corner: " + corner);
        }
    }

    //TODO This is hardcoded to only follow "has" and "has-child" transitive relationships
    private  List<ThingWithQueryParams> BuildSearchList(List<Thing> q, bool reverse = false)
    {

        List<ThingWithQueryParams> thingsToExamine = new();
        int maxHops = 20;
        int hopCount = 0;
        foreach (Thing t in q)
            thingsToExamine.Add(new ThingWithQueryParams
            {
                thing = t,
                hopCount = hopCount,
                weight = 1,
                reachedWith = null
            }); ;
        hopCount++;
        int currentEnd = thingsToExamine.Count;
        for (int i = 0; i < thingsToExamine.Count; i++)
        {
            Thing t = thingsToExamine[i].thing;
            float curWeight = thingsToExamine[i].weight;
            int curCount = thingsToExamine[i].haveCount;
            Thing reachedWith = thingsToExamine[i].reachedWith;

            foreach (Relationship r in t.RelationshipsFrom)  //has-child et al
                if ((r.relType.HasAncestorLabeled("has-child") && !reverse) ||
                    (r.relType.HasAncestorLabeled("has") && reverse) ||
                   (r.relType.HasAncestorLabeled("is") && reverse))
                {
                    if (thingsToExamine.FindFirst(x => x.thing == r.source) is ThingWithQueryParams twgp)
                    {
                        twgp.hitCount++;
                    }
                    else
                    {
                        bool corner = !ThingInTree(r.relType, thingsToExamine[i].reachedWith) &&
                            thingsToExamine[i].reachedWith != null;
                        if (corner)
                        { }
                        thingsToExamine[i].corner |= corner;
                        ThingWithQueryParams thingToAdd = new ThingWithQueryParams
                        {
                            thing = r.source,
                            hopCount = hopCount,
                            weight = curWeight * r.Weight,
                            reachedWith = r.relType,
                        };
                        thingsToExamine.Add(thingToAdd);
                        //if things have counts, they are multiplied
                        int val = GetCount(r.reltype);
                        thingToAdd.haveCount = curCount * val;
                    }
                }

            foreach (Relationship r in t.Relationships) //has-a et al
                if ((r.relType.HasAncestorLabeled("has-child") && reverse) ||
                    (r.relType.HasAncestorLabeled("has") && !reverse))
                {
                    if (thingsToExamine.FindFirst(x => x.thing == r.target) is ThingWithQueryParams twqp)
                    {
                        twqp.hitCount++;
                    }
                    else
                    {
                        bool corner = !ThingInTree(r.relType, thingsToExamine[i].reachedWith) &&
                            thingsToExamine[i].reachedWith != null;
                        if (corner)
                        { }
                        thingsToExamine[i].corner |= corner;
                        ThingWithQueryParams thingToAdd = new ThingWithQueryParams
                        {
                            thing = r.target,
                            hopCount = hopCount,
                            weight = curWeight * r.Weight,
                            reachedWith = r.relType,
                        };
                        thingsToExamine.Add(thingToAdd);
                        //if things have counts, they are multiplied
                        int val = GetCount(r.reltype);
                        thingToAdd.haveCount = curCount * val;
                    }
                }
            if (i == currentEnd - 1)
            {
                hopCount++;
                currentEnd = thingsToExamine.Count;
                if (hopCount > maxHops) break;
            }
        }
        return thingsToExamine;
    }

    private List<Relationship> GetAllRelationshipsInt(List<ThingWithQueryParams> thingsToExamine)
    {
        List<Relationship> result = new();
        for (int i = 0; i < thingsToExamine.Count; i++)
        {
            Thing t = thingsToExamine[i].thing;
            if (t == null) continue;
            int haveCount = thingsToExamine[i].haveCount;
            List<Relationship> relationshipsToAdd = null;
            relationshipsToAdd = new();
            relationshipsToAdd.AddRange(t.Relationships);
            //relationshipsToAdd.AddRange(t.RelationshipsFrom);
            //relationshipsToAdd.AddRange(t.RelationshipsAsTypeWriteable);
            foreach (Relationship r in relationshipsToAdd)
            {
                if (r.reltype == Thing.HasChild) continue;

                //only add the new relatinoship to the list if it is not already in the list
                bool ignoreSource = thingsToExamine[i].hopCount > 1;
                Relationship existing = result.FindFirst(x => RelationshipsAreEqual(x, r, ignoreSource));
                if (existing != null) continue;

                if (haveCount > 1)
                {
                    //this creates a temporary relationship so suzie has 2 arm, arm has 5 fingers, return suzie has 10 fingers
                    //this (transient) relationshiop doesn't exist in the UKS
                    Relationship r1 = new Relationship(r);
                    Thing newCountType = GetOrAddThing((GetCount(r.reltype) * haveCount).ToString(), "number");

                    //hack for numeric labels
                    Thing rootThing = r1.reltype;
                    //TODO  reenable
                    if (r.relType.Label.Contains("."))
                        rootThing = GetOrAddThing(r.relType.Label.Substring(0, r.relType.Label.IndexOf(".")));
                    Thing newRelType = CreateSubclass(rootThing, new List<Thing> { newCountType });
                    r1.reltype = newRelType;
                    result.Add(r1);
                }
                else
                    result.Add(r);
            }
        }
        return result;
    }

    private void RemoveConflictingResults(List<Relationship> result)
    {
        for (int i = 0; i < result.Count; i++)
        {
            Relationship r1 = result[i];
            for (int j = i + 1; j < result.Count; j++)
            {
                Relationship r2 = result[j];
                if (RelationshipsAreExclusive(r1, r2))
                {
                    result.RemoveAt(j);
                    j--;
                }

            }
        }
    }
    private void RemoveFalseConditionals(List<Relationship> result)
    {
        for (int i = 0; i < result.Count; i++)
        {
            Relationship r1 = result[i];
            if (!ConditionsAreMet(r1.Clauses, r1))
            {
                failedConditions.Add(r1);
                result.RemoveAt(i);
                i--;
            }
            else
            {
                succeededConditions.Add(r1);
            }
        }
    }

    /// <summary>
    /// Filters a list of Relationships returning only those with at least one component which  has an ancestor in the list of Ancestors
    /// </summary>
    /// <param name="result">List of Relationships from a previous Query</param>
    /// <param name="ancestors">Filter</param>
    /// <returns></returns>
    public IList<Relationship> FilterResults(List<Relationship> result, List<Thing> ancestors)
    {
        List<Relationship> retVal = new();
        if (ancestors == null || ancestors.Count == 0)
            return result;
        foreach (Relationship r in result)
            if (RelationshipHasAncestor(r, ancestors))
                retVal.Add(r);
        return retVal;
    }

    private bool RelationshipHasAncestor(Relationship r, List<Thing> ancestors)
    {
        foreach (Thing ancestor in ancestors)
        {
            if (r.source.HasAncestor(ancestor)) return true;
            if (r.relType.HasAncestor(ancestor)) return true;
            if (r.target.HasAncestor(ancestor)) return true;
        }
        return false;
    }

    int GetCount(Thing t)
    {
        int retVal = 1;
        foreach (Relationship r in t.Relationships)
            if (r.relType.Label == "is")
                if (int.TryParse(r.target.Label, out int val))
                    return val;
        return retVal;
    }

           //find all the things containing the sequence of attributes
 private bool HasSequence(IList<Relationship> relationships, List<Thing> targetAttributes)
    {
        //TODO modify to find closest match, return the matching score instead of bool
        //TODO this requires that all entries in a sequence must be adjascent without any intervening extraneous relationships
        //TODO some sequences (spatial) are circular and the search must loop back to the beginning (not for anything temporal)
        IList<Relationship> sortedReferences = relationships.OrderBy(x => x.relType.Label).ToList();

        //TODO the for loop below allows for matching a partial which does not start at the beginning of the stored sequence
        int i = 0;
        //for (int i = 0; i < sortedReferences.Count - targetAttributes.Count + 1; i++)
        {
            for (int j = 0; j < targetAttributes.Count; j++)
            {
                if (sortedReferences[i + j].target != targetAttributes[j]) goto misMatch;
            }
            return true;
        misMatch:;
        }
        return false;
    }

    /// <summary>
    /// Returns a list of Things which have all the target attributes as Relationships
    /// </summary>
    /// <param name="targetAttributes">An ordered list of Things which must occur as attributes in the search target</param>
    /// <returns>All the Things which match the criteria</returns>
    public List<Thing> HasSequence(List<Thing> targetAttributes)
    {
        //get a list of all things with the given attributes
        List<Thing> retVal = new();
        foreach (Thing t in targetAttributes)
            foreach (Relationship r in t.RelationshipsFrom)
                if (r.relType.HasAncestorLabeled("has"))
                    if (!retVal.Contains(r.source))
                        retVal.Add(r.source);

        //remove the onew without the sequence
        for (int i = 0; i < retVal.Count; i++)
        {
            if (!HasSequence(retVal[i].Relationships, targetAttributes))
            {
                retVal.RemoveAt(i);
                if (i != 0)
                    i--;
            }
        }
        return retVal;
    }

    //TODO add parameter to allow some number of misses
    private  bool HasAllAttributes(Thing t, List<Thing> targetAttributes)
    {
        List<Thing> thingAttributes = GetAllAttributes(t);
        foreach (Thing t2 in targetAttributes)
            if (!thingAttributes.Contains(t2) && !t.AncestorList().Contains(t2)) return false;
        return true;
    }
    /// <summary>
    /// Returns a list of Things which have ALL the given attributes (IS Relationships)
    /// </summary>
    /// <param name="attributes">List of Things</param>
    /// <returns></returns>
    public List<Thing> FindThingsWithAttributes(List<Thing> attributes)
    {
        List<Thing> retVal = new();
        List<Thing> attribOwners = new List<Thing>();
        foreach (Thing t in attributes)
        {
            foreach (Relationship r in t.RelationshipsFrom)
                if (r.reltype.Label == "is")
                {
                    attribOwners.Add(r.source);
                }
        }
        List<ThingWithQueryParams> results = BuildSearchList(attribOwners);
        foreach (var t in results)
        {
            if (HasAllAttributes(t.thing, attributes))
                retVal.Add(t.thing);
        }
        return retVal;
    }


    bool ConditionsAreMet(List<Clause> clauses, Relationship query)
    {
        if (clauses.Count == 0) return true;
        //if (StackContains("ConditionsAreMet",1)) return true;
        foreach (Clause c in clauses)
        {
            if (c.clauseType.Label.ToLower() != "if") continue;
            Relationship r = c.clause;
            QueryRelationship q = new(r);
            if (query.source != null && query.source.AncestorList().Contains(q.source))
                q.source = query.source;
            if (query.source != null && query.source.AncestorList().Contains(q.target))
                q.target = query.target;
            if (query.target != null && query.target.AncestorList().Contains(q.source))
                q.source = query.target;
            var qResult = Relationship.GetRelationship(q);
            if (qResult != null && qResult.Weight < 0.8)
                return false;
            if (qResult == null)
            {
                failedConditions.Add(q);
                return false;
            }
            else
                succeededConditions.Add(q);
        }
        return true;
    }
    /// <summary>
    /// Returns a list of Relationships which were false in the previous query
    /// </summary>
    /// <returns></returns>

    public List<Relationship> WhyNot()
    {
        return failedConditions;
    }
    /// <summary>
    /// Returns a list of Relatioships which were true in the previous query
    /// </summary>
    /// <returns></returns>
    public List<Relationship> Why()
    {
        return succeededConditions;
    }
}
