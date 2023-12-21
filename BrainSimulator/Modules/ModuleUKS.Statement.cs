﻿using Emgu.CV.Dnn;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Documents;

namespace BrainSimulator.Modules
{
    public partial class ModuleUKS
    {
        //this overload lets you pass in
        //a string or a thing for the first three parameters
        //and a string, Thing, or list of string or Thing for the last 3
        public Relationship AddStatement(
            object oSource, object oRelationshipType, object oTarget,
            object oSourceProperties = null,
            object oTypeProperties = null,
            object oTargetProperties = null
                        )
        {
            try
            {
                Thing source = ThingFromObject(oSource);
                Thing relationshipType = ThingFromObject(oRelationshipType, "RelationshipType",source);
                Thing target = ThingFromObject(oTarget);

                List<Thing> sourceModifiers = ThingListFromObject(oSourceProperties);
                List<Thing> relationshipTypeModifiers = ThingListFromObject(oTypeProperties, "RelationshipType");
                List<Thing> targetModifiers = ThingListFromObject(oTargetProperties);

                Relationship theRelationship = AddStatement(source, relationshipType, target, sourceModifiers, relationshipTypeModifiers, targetModifiers);
                return theRelationship;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public Relationship AddStatement(
                        Thing source, Thing relType, Thing target,
                        List<Thing> sourceProperties,
                        List<Thing> typeProperties,
                        List<Thing> targetProperties
                )
        {
            if (source == null || relType == null) return null;

            QueryRelationship r = CreateTheRelationship(ref source, ref relType, ref target, ref sourceProperties, typeProperties, ref targetProperties);

            //does this relationship already exist (without conditions)?
            Relationship existing = Relationship.GetRelationship(r);
            if (existing != null)
            {
                WeakenConflictingRelationships(source, existing);
                existing.Fire();
                return existing;
            }

            //fire all parents 
            //var y = SearchRelationships(r, true,false);
            //foreach (Relationship r2 in y)
            //    r2.Fire();

            //will this cause a circular relationship?
            if (r.reltype?.Label == "has-child")
            {
                if (r.source.AncestorList().Contains(target) || r.source == r.target)
                {
                    Debug.WriteLine($"Circular Reference: {r.ToString()}");
                    return null;
                }
            }

            WeakenConflictingRelationships(source, r);

            //var x = SearchRelationships(r, false);
            //if (x == null) return null;
            //if (x.Count > 0) return x[0];

            r.targetProperties = new();
            r.typeProperties = new();
            r.sourceProperties = new();

            Relationship rSave = new Relationship(r);

            WriteTheRelationship(rSave);
            if (rSave.relType != null && HasProperty(rSave.relType, "commutative"))
            {
                Relationship rSave1 = new Relationship(rSave);
                (rSave1.source, rSave1.target) = (rSave1.target, rSave1.source);
                rSave1.clauses.Clear();
                WriteTheRelationship(rSave1);
            }

            //if this is adding a child relationship, remove any unknownObject parent
            ClearExtraneousParents(rSave.source);
            ClearExtraneousParents(rSave.T);
            ClearExtraneousParents(rSave.relType);
            ClearRedundancyInAncestry(rSave.target);

            return rSave;
        }
        void ClearRedundancyInAncestry(Thing t)
        {
            if (t == null) return;
            //if a direct parent has an ancestor which is also another direct parent, remove that 2nd direct parent
            var parents = t.Parents;
            foreach (Thing parent in parents)
            {
                var p = parent.AncestorList();
                p.RemoveAt(0); //don't check yourself
                foreach (Thing ancestor in p)
                {
                    if (parents.Contains(ancestor))
                        t.RemoveParent(ancestor);
                }
            }
        }

        public QueryRelationship CreateTheRelationship(ref Thing source, ref Thing relType, ref Thing target, ref List<Thing> sourceProperties, List<Thing> typeProperties, ref List<Thing> targetProperties)
        {
            Thing inverseType1 = CheckForInverse(relType);
            //if this relationship has an inverse, switcheroo so we are storing consistently in one direction
            if (inverseType1 != null)
            {
                (source, target) = (target, source);
                (sourceProperties, targetProperties) = (targetProperties, sourceProperties);
                relType = inverseType1;
            }
            QueryRelationship r = new()
            {
                relType = relType,
                source = source,
                T = target,
                sourceProperties = (sourceProperties is null) ? new() : sourceProperties,
                targetProperties = (targetProperties is null) ? new() : targetProperties,
                typeProperties = (typeProperties is null) ? new() : typeProperties,
            };

            //handle pronouns in statements
            //if (HandlePronouns(r)) return r;

            //if this is a "has" relationship see if a new instance of the objecte is needed?
            //TODO properly find if the target already exists or needs to have an instance created
            if (r.relType != null && HasProperty(r.reltype, "makeInstance") && r.T != null || r.targetProperties.Count > 0)
            {
                //does the parent of this thing already reference some sort of target?
                //if a dog has a specific kind of tail, we need to create an instance of that specific dog
                //TODO properly find if the target already exists or needs a new instance
                if (r.source != null)
                {
                    foreach (Thing t in r.target.Parents)
                    {
                        foreach (Relationship r1 in t.Relationships)
                            if (r1.target.Parents.Contains(r.T))
                            {
                                r.T = r1.target;
                            }
                    }
                }
                r.T = CreateInstanceOf(r.T, r.targetProperties);
            }

            if (r.sourceProperties.Count > 0 && r.source != null)
            {
                //does the parent of this thing already reference some sort of target?
                //if a dog has a specific kind of tail, we need to create an instance of that specific
                //foreach (Thing t in r.source.Parents)
                //{
                //    foreach (Relationship r1 in t.Relationships)
                //        if (r1.source.Parents.Contains(r.source))
                //        {
                //            r.source = r1.source;
                //        }
                //}
                Thing newInstance = CreateInstanceOf(r.source, r.sourceProperties);
                if(r.target != newInstance)
                    r.source = newInstance; 
            }

            if (r.reltype != null && typeProperties != null && typeProperties.Count > 0)
            {
                r.reltype = CreateInstanceOf(r.relType, r.typeProperties);
            }
            r.source?.SetFired();
            r.target?.SetFired();
            r.relType?.SetFired();


            return r;
        }

        private void WeakenConflictingRelationships(Thing source, Relationship r1)
        {
            //does this new relationship conflict with an existing relationship)?
            for (int i = 0; i < source?.Relationships.Count; i++)
            {
                Relationship sourceRel = source.Relationships[i];
                if (sourceRel == r1)
                {
                    //strengthen this relationship
                    //sourceRel.weight = 1;
                    r1.weight +=  (1-r1.weight)/2.0f;
                    //sourceRel.weight = Math.Clamp(sourceRel.weight + .2f, -1f, 1f);
                    r1.Fire();
                }
                else if (RelationshipsAreExclusive(r1, sourceRel))
                {
                    //special case for "not"
                    if (GetAttributes(r1.reltype)?.FindFirst(x => x.Label == "not") != null)
                    {
                        source.RemoveRelationship(sourceRel);
                        i--;
                    }
                    else
                    {
                        if (r1.weight == 1 && sourceRel.weight == 1)
                            sourceRel.weight = .5f;
                        else
                            sourceRel.weight = Math.Clamp(sourceRel.weight - .2f, -1, 1);
                        if (sourceRel.weight <= 0)
                        {
                            source.RemoveRelationship(sourceRel);
                            i--;
                        }
                    }
                }
            }
        }

        public static void WriteTheRelationship(Relationship r)
        {
            if (r.source == null && r.target == null) return;
            if (r.target == null)
            {
                lock (r.source.RelationshipsWriteable)
                    lock (r.relType.RelationshipsFromWriteable)
                    {
                        if (!r.source.RelationshipsWriteable.Contains(r))
                            r.source.RelationshipsWriteable.Add(r);
                        if (!r.reltype.RelationshipsAsTypeWriteable.Contains(r))
                            r.reltype.RelationshipsAsTypeWriteable.Add(r);
                    }
            }
            else if (r.source == null)
            {
                lock (r.target.RelationshipsWriteable)
                    lock (r.relType.RelationshipsFromWriteable)
                    {
                        if (!r.target.RelationshipsWriteable.Contains(r))
                            r.target.RelationshipsFromWriteable.Add(r);
                        if (!r.reltype.RelationshipsAsTypeWriteable.Contains(r))
                            r.reltype.RelationshipsAsTypeWriteable.Add(r);
                    }
            }
            //else if (r.relType == null)
            //{ //this case is not allowed
            //    lock (r.target.RelationshipsWriteable)
            //        lock (r.source.RelationshipsFromWriteable)
            //        {
            //            r.target.RelationshipsFromWriteable.Add(r);
            //            r.source.RelationshipsWriteable.Add(r);
            //        }
            //}
            else
            {
                lock (r.source.RelationshipsWriteable)
                    lock (r.target.RelationshipsFromWriteable)
                        lock (r.relType.RelationshipsFromWriteable)
                        {
                            if (!r.source.RelationshipsWriteable.Contains(r))
                                r.source.RelationshipsWriteable.Add(r);
                            if (!r.target.RelationshipsWriteable.Contains(r))
                                r.target.RelationshipsFromWriteable.Add(r);
                            if (!r.reltype.RelationshipsAsTypeWriteable.Contains(r))
                                r.reltype.RelationshipsAsTypeWriteable.Add(r);
                        }
            }
        }

        void ClearExtraneousParents(Thing t)
        {
            if (t == null) return;
            //if a thing has more than one parent and one of them is unkonwnObject, 
            //then the unknownObject relationship is unnedessary
            if (t != null && t.Parents.Count > 1)
                t.RemoveParent(ThingLabels.GetThing("unknownObject"));
        }

        public Thing SubclassExists(Thing t,List<Thing> targetModifiers)
        {
            //if instance already exists, return it
            if (t == null) return null;
            foreach (Thing t2 in t.Descendents)
            {
                foreach (Thing t3 in targetModifiers)
                {
                    if (t2.Relationships.FindFirst(x => x.T == t3) == null)
                        goto notFound;
                }
                return t2;
            notFound:
                continue;
            }
            return null;
        }


        Thing CreateInstanceOf(Thing t, List<Thing> modifiers)
        {
            Thing t2 = SubclassExists(t, modifiers);
            if (t2 != null) return t2;

            string newLabel = t.Label;
            foreach (Thing t1 in modifiers)
                newLabel += "." + t1.Label;

            Thing retVal = GetOrAddThing(newLabel, t);
            foreach (Thing t1 in modifiers)
            {
                AddStatement(retVal, "is", t1);
            }
            ClearExtraneousParents(t);
            return retVal;
        }
        private Thing CheckForInverse(Thing relationshipType)
        {
            if (relationshipType == null) return null;
            Relationship inverse = relationshipType.Relationships.FindFirst(x => x.reltype.Label == "inverseOf");
            if (inverse != null) return inverse.target;
            //use the bwlow if inverses ae 2-way.  Without this, there is a one-way translation
            //inverse = relationshipType.RelationshipsBy.FindFirst(x => x.reltype.Label == "inverseOf");
            //if (inverse != null) return inverse.source;
            return null;
        }
        public static List<Thing> FindCommonParents(Thing t, Thing t1)
        {
            List<Thing> commonParents = new List<Thing>();
            foreach (Thing p in t.Parents)
                if (t1.Parents.Contains(p))
                    commonParents.Add(p);
            return commonParents;
        }

    }
}
