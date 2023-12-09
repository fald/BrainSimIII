﻿//
// PROPRIETARY AND CONFIDENTIAL
// Brain Simulator 3 v.1.0
// © 2022 FutureAI, Inc., all rights reserved
//


using System;
using System.Collections.Generic;
using System.Linq;
using BrainSimulator.Modules;
using System.Collections.Concurrent;

namespace BrainSimulator
{
    //a thing is anything, physical object, attribute, word, action, etc.
    public class Thing
    {
        private List<Relationship> relationships = new List<Relationship>(); //synapses to "has", "is", others
        private List<Relationship> relationshipsFrom = new List<Relationship>(); //synapses from
        public IList<Relationship> RelationshipsNoCount { get { lock (relationships) { return new List<Relationship>(relationships.AsReadOnly()); } } }
        public List<Relationship> RelationshipsWriteable { get => relationships; }
        public IList<Relationship> RelationshipsFrom { get { lock (relationshipsFrom) { return new List<Relationship>(relationshipsFrom.AsReadOnly()); } } }
        public List<Relationship> RelationshipsFromWriteable { get => relationshipsFrom; }

        private string label = ""; //this is just for convenience in debugging 
        object value;
        public int useCount = 0;
        public long lastFired = 0;
        public DateTime lastFiredTime = new();

        public object V
        {
            get => value;
            set
            {
                if (!value.GetType().IsSerializable)
                    throw new ArgumentException("Cannot set nonserializable value");
                this.value = value;
            }
        }

        static ConcurrentDictionary<string, Thing> labelList = new ConcurrentDictionary<string, Thing>();
        public static Thing GetThing(string label)
        {
            Thing retVal = null;
            if (labelList.TryGetValue(label.ToLower(), out retVal)) { }
            return retVal;
        }
        public static string AddThingLabel(string newLabel, Thing t)
        {
            //sets a label and appends/increments trailing digits in the event of collisions
            if (newLabel == "") return newLabel; //don't index empty lables
            labelList.TryRemove(t.label.ToLower(), out Thing dummy);
            int curDigits = -1;
            string baseString = newLabel;
            //This code allows you to put a * at the end of a label and it will auto-increment
            if (newLabel.EndsWith("*"))
            {
                curDigits = 0;
                baseString = newLabel.Substring(0, newLabel.Length - 1);
                newLabel = baseString + curDigits;
            }

            //autoincrement in the event of name collisions
            while (!labelList.TryAdd(newLabel.ToLower(), t))
            {
                curDigits++;
                newLabel = baseString + curDigits;
            }
            return newLabel;
        }
        public static void ClearLabelList()
        {
            labelList.Clear();
            hasChildType = null;
        }
        public static List<Thing> AllThingsInLabelList()
        {
            List<Thing> retVal = new();
            foreach (Thing thing in labelList.Values) { retVal.Add(thing); }
            return retVal;
        }

        //even with no references, don't delete this because the debugger uses it
        public override string ToString()
        {
            string retVal = label + ": " + useCount;
            if (Relationships.Count > 0)
            {
                retVal += " {";
                foreach (Relationship l in Relationships)
                    retVal += l.T?.label + ",";
                retVal += "}";
            }
            return retVal;
        }


        //This hack is needed because add-parent/add-child rely on knowledge of the has-child relationship which may not exist yet
        static Thing hasChildType;
        static Thing HasChild
        {
            get
            {
                if (hasChildType == null)
                {
                    hasChildType = GetThing("has-child");
                    if (hasChildType == null)
                    {
                        Thing thingRoot = GetThing("Thing");
                        if (thingRoot == null) return null;
                        Thing relTypeRoot = GetThing("RelationshipType");
                        if (relTypeRoot == null)
                        {
                            hasChildType = new Thing() { Label = "has-child" };
                            relTypeRoot = new Thing() { Label = "RelationshipType" };
                            thingRoot.AddRelationship(relTypeRoot, hasChildType);
                            relTypeRoot.AddRelationship(hasChildType, hasChildType);
                        }
                    }
                }
                return hasChildType;
            }
        }

        public string Label
        {
            get => label;
            set
            {
                if (value == label) return; //label is unchanged
                label = AddThingLabel(value, this);
            }
        }

        private IList<Thing> RelationshipsOfType(Thing relType, bool useRelationshipFrom = false)
        {
            IList<Thing> retVal = new List<Thing>();
            if (!useRelationshipFrom)
            {
                lock (relationshipsFrom)
                {
                    foreach (Relationship r in relationships)
                        if (r.relType != null && r.relType == relType && r.source == this)
                            retVal.Add(r.target);
                }
            }
            else
            {
                lock (relationshipsFrom)
                {
                    foreach (Relationship r in relationshipsFrom)
                        if (r.relType != null && r.relType == relType && r.target == this)
                            retVal.Add(r.source);
                }
            }
            return retVal;
        }
        private bool IsKindOf(Thing thingType)
        {
            if (this == thingType) return true;
            foreach (Thing t in this.Parents)
                if (t.IsKindOf(thingType)) return true;
            return false;
        }


        public IList<Thing> Parents { get => RelationshipsOfType(GetThing("has-Child"), true); }

        public IList<Thing> Children { get => RelationshipsOfType(GetThing("has-Child"), false); }

        public IList<Relationship> Relationships
        {
            get
            {
                lock (relationships)
                {
                    foreach (Relationship r in relationships)
                        r.misses++;
                    return new List<Relationship>(relationships.AsReadOnly());
                }
            }
        }

        public IList<Relationship> RelationshipsWithoutChildren
        {
            get
            {
                List<Relationship> retVal = new();
                foreach (Relationship r in Relationships)
                    if (r.reltype == null || Relationship.TrimDigits(r.relType.Label) != "has-child") retVal.Add(r);
                return retVal;
            }
        }


        /// ////////////////////////////////////////////////////////////////////////////
        //Handle the ancestors and descendents of a Thing
        //////////////////////////////////////////////////////////////
        public IList<Thing> AncestorList()
        {
            return FollowTransitiveRelationships(hasChildType, true);
        }

        public IEnumerable<Thing> Ancestors
        {
            get
            {
                IList<Thing> ancestors = AncestorList();
                for (int i = 0; i < ancestors.Count; i++)
                {
                    Thing child = ancestors[i];
                    yield return child;
                }
            }
        }

        public bool HasAncestorLabeled(string label)
        {
            return HasAncestor(GetThing(label));
        }
        public bool HasAncestor(Thing t)
        {
            return FollowTransitiveRelationships(hasChildType, true, t).Count != 0;
        }

        public int GetDescendentsCount()
        {
            return DescendentsList().Count;
        }
        public IList<Thing> DescendentsList()
        {
            return FollowTransitiveRelationships(hasChildType, false);
        }

        //recursively gets all descendents
        public IEnumerable<Thing> Descendents
        {
            get
            {
                IList<Thing> descendents = DescendentsList();
                for (int i = 0; i < descendents.Count; i++)
                {
                    Thing child = descendents[i];
                    yield return child;
                }
            }
        }

        //Follow chain of relationships with relType
        public IList<Thing> FollowTransitiveRelationships(Thing relType, bool followUpwards = true, Thing searchTarget = null)
        {
            List<Thing> retVal = new();
            retVal.Add(this);
            for (int i = 0; i < retVal.Count; i++)
            {
                Thing t = retVal[i];
                IList<Relationship> relationshipsToFollow = followUpwards?t.RelationshipsFrom:t.Relationships;
                foreach (Relationship r in relationshipsToFollow)
                {
                    Thing thingToAdd = followUpwards ? r.source : r.target;
                    if (r.reltype == relType)
                    { 
                        if (searchTarget == thingToAdd) return retVal;
                        if (!retVal.Contains(thingToAdd))
                            retVal.Add(thingToAdd);
                    }
                }
            }
            if (searchTarget != null) retVal.Clear();
            return retVal;
        }


        public void SetFired(Thing t = null)
        {
            if (t != null)
            {
                t.lastFiredTime = DateTime.Now;
                t.useCount++;
            }
            else
            {
                lastFiredTime = DateTime.Now;
                useCount++;
            }
        }

        /// <summary>
        /// ////////////////////////////////////////////////////////////////
        /// </summary>

        //RELATIONSHIPS

        //add a relationship from this thing to the specified thing
        public Relationship AddRelationship(Thing target, Thing relationshipType)
        {
            if (relationshipType == null)
                return null;

            //does the relationship already exist?
            Relationship r = HasRelationship(target, relationshipType);
            if (r != null)
            {
                //AdjustRelationship(r.T);
                return r;
            }
            r = new Relationship()
            {
                relType = relationshipType,
                source = this,
                T = target,
            };
            if (target != null)
            {
                lock (relationships)
                    lock (target.relationshipsFrom)
                        lock (relationshipType.relationshipsFrom)
                        {
                            RelationshipsWriteable.Add(r);
                            target.RelationshipsFromWriteable.Add(r);
                            relationshipType.RelationshipsFromWriteable.Add(r);
                        }
            }
            else
            {
                lock (relationships)
                    lock (relationshipType.relationshipsFrom)
                    {
                        RelationshipsWriteable.Add(r);
                        relationshipType.RelationshipsFromWriteable.Add(r);
                    }
            }
            return r;
        }
        private Relationship HasRelationship(Thing target, Thing relationshipType)
        {
            foreach (Relationship r in relationships)
            {
                if (r.source == this && r.target == target && r.reltype == relationshipType)
                    return r;
            }
            return null;
        }

        public void RemoveRelationship(Relationship r)
        {
            if (r == null) return;
            if (r.reltype == null) return;
            if (r.source == null)
            {
                lock (r.relType.RelationshipsFromWriteable)
                {
                    lock (r.target.RelationshipsFromWriteable)
                    {
                        r.relType.RelationshipsFromWriteable.RemoveAll(x => x.source == r.source && x.reltype == r.reltype && x.target == r.target); ;
                        r.target.RelationshipsFromWriteable.RemoveAll(x => x.source == r.source && x.reltype == r.reltype && x.target == r.target); ;
                    }
                }
            }
            else if (r.target == null)
            {
                lock (r.source.RelationshipsWriteable)
                {
                    lock (r.relType.RelationshipsFromWriteable)
                    {
                        r.source.RelationshipsWriteable.RemoveAll(x => x.source == r.source && x.reltype == r.reltype && x.target == r.target); ;
                        r.relType.RelationshipsFromWriteable.RemoveAll(x => x.source == r.source && x.reltype == r.reltype && x.target == r.target); ;
                    }
                }
            }
            else
            {
                lock (r.source.RelationshipsWriteable)
                {
                    lock (r.relType.RelationshipsFromWriteable)
                    {
                        lock (r.target.RelationshipsFromWriteable)
                        {
                            r.source.RelationshipsWriteable.RemoveAll(x=>x.source ==r.source && x.reltype == r.reltype && x.target == r.target);
                            r.relType.RelationshipsFromWriteable.RemoveAll(x => x.source == r.source && x.reltype == r.reltype && x.target == r.target);
                            r.target.RelationshipsFromWriteable.RemoveAll(x => x.source == r.source && x.reltype == r.reltype && x.target == r.target);
                        }
                    }
                }
            }
            foreach (ClauseType c in r.clauses)
                RemoveRelationship(c.clause);
        }

        public Relationship HasRelationship(Thing t)
        {
            foreach (Relationship L in Relationships)
                if (L.T == t) return L;
            return null;
        }

        public Thing HasRelationshipWithParent(Thing t)
        {
            foreach (Relationship L in Relationships)
                if (L.T.Parents.Contains(t)) return L.T;
            return null;
        }

        public Thing HasRelationshipWithAncestorLabeled(string s)
        {
            foreach (Relationship L in Relationships)
            {
                if (L.T != null)
                {
                    Thing t = L.T.AncestorList().FindFirst(x => x.Label == s);
                    if (t != null) return L.T;
                }
            }
            return null;
        }


        public void RemoveRelationship(Thing t2, Thing relationshipType)
        {
            Relationship r = new() { source = this, reltype = relationshipType, target = t2 };
            RemoveRelationship(r);
        }


        //returns all the matching refrences
        public List<Relationship> GetRelationshipsWithAncestor(Thing t)
        {
            List<Relationship> retVal = new List<Relationship>();
            lock (relationships)
            {
                for (int i = 0; i < Relationships.Count; i++)
                {
                    if (Relationships[i].T.HasAncestor(t))
                    {
                        retVal.Add(Relationships[i]);
                    }
                }
                return retVal.OrderBy(x => -x.Value).ToList();
            }
        }

        public List<Relationship> GetRelationshipByWithAncestor(Thing t)
        {
            List<Relationship> retVal = new List<Relationship>();
            for (int i = 0; i < relationshipsFrom.Count; i++)
            {
                if (relationshipsFrom[i].source.HasAncestor(t))
                {
                    retVal.Add(relationshipsFrom[i]);
                }
            }
            return retVal.OrderBy(x => -x.Value).ToList();
        }

        public void AddParent(Thing newParent)
        {
            if (newParent == null) return;
            if (!Parents.Contains(newParent))
            {
                newParent.AddRelationship(this, HasChild);
            }
        }

        public void RemoveParent(Thing t)
        {
            Relationship r = new() { source = t, reltype = hasChildType, target = this };
            t.RemoveRelationship(r);
        }

        public Relationship AddChild(Thing t)
        {
            return AddRelationship(t, HasChild);
        }

        public void RemoveChild(Thing t)
        {
            Relationship r = new() { source = this, reltype = hasChildType, target = t };
            RemoveRelationship(r);
        }

    }

    //this is a modification of Thing which is used to store and retrieve the KB in XML
    //it eliminates circular references by replacing Thing references with int indexed into an array and makes things much more compact
    public class SThing
    {
        public string label = ""; //this is just for convenience in debugging and should not be used
        public List<SRelationship> relationships = new();
        object value;
        public object V { get => value; set => this.value = value; }
        public int useCount;
    }
}
