using System;
using System.Collections.Generic;
using Util;

namespace LynnaLib
{
    /// Represents an "INTERACID_TREASURE" value (1 byte). This can be used to lookup
    /// a "TreasureObject" which has an additional subID.
    public class TreasureGroup : ProjectIndexedDataType {
        TreasureObject[] treasureObjectCache = new TreasureObject[256];
        Data dataStart;


        internal TreasureGroup(Project p, int index) : base(p, index) {
            if (Index >= Project.NumTreasures)
                throw new InvalidTreasureException(
                        string.Format("Treasure {0:X2} doesn't exist!", Index));

            DetermineDataStart();
        }

        // Properties

        public int NumTreasureObjectSubids {
            get {
                Data data = dataStart;
                if (!UsesPointer)
                    return 1;
                try {
                    data = Project.GetData(data.GetValue(0));
                }
                catch (InvalidLookupException) {
                    return 0;
                }
                return TraverseSubidData(ref data, 256);
            }
        }

        bool UsesPointer {
            get { return dataStart.CommandLowerCase == "m_treasurepointer"; }
        }


        // Methods

        public TreasureObject GetTreasureObject(int subid) {
            if (treasureObjectCache[subid] == null) {
                Data data = GetSubidBaseData(subid);
                if (data == null)
                    return null;
                treasureObjectCache[subid] = new TreasureObject(this, subid, data);
            }
            return treasureObjectCache[subid];
        }

        public TreasureObject AddTreasureObjectSubid() {
            if (NumTreasureObjectSubids >= 256)
                return null;

            Func<int, bool, string> ConstructTreasureSubidString = (subid, inSubidTable) => {
                string prefix = string.Format("/* ${0:x2} */ ", Index);
                string body = string.Format("$00, $00, $ff, $00, TREASURE_OBJECT_{0}_{1:x2}",
                        Project.TreasureMapping.ByteToString(Index).Substring(9),
                        subid);
                if (inSubidTable)
                    return "\tm_TreasureSubid " + body;
                else
                    return "\t" + prefix + "m_TreasureSubid   " + body;
            };

            if (NumTreasureObjectSubids == 0) {
                // This should only happen when the treasure is using "m_treasurepointer", but has
                // a null pointer. So rewrite that line with a blank treasure.
                dataStart.FileParser.InsertParseableTextAfter(dataStart, new string[] {
                    ConstructTreasureSubidString(0, false)
                });
                dataStart.Detach();

                // Update dataStart (since the old data was deleted)
                DetermineDataStart();

                return GetTreasureObject(0);
            }

            if (!UsesPointer) {
                // We need to create a pointer for the subid list and move the old data to the start
                // of the list. Be careful to ensure that the old data objects are moved, and not
                // deleted, so that we don't break the TreasureObject's that were built on them.
                // Create pointer
                FileParser parser = dataStart.FileParser;
                string labelName = Project.GetUniqueLabelName(
                        string.Format("treasureObjectData{0:x2}", Index));
                parser.InsertParseableTextBefore(dataStart, new string[] { string.Format(
                    "\t/* ${0:x2} */ m_TreasurePointer {1}", Index, labelName)
                });

                dataStart.Detach();

                // Create label
                parser.InsertParseableTextAfter(null, new string[] { labelName + ":" });

                // Create "m_BeginTreasureSubids" macro
                parser.InsertParseableTextAfter(null, new string[] { string.Format(
                            "\tm_BeginTreasureSubids " + Project.TreasureMapping.ByteToString(Index))
                });

                // Move old data to after the label
                parser.InsertComponentAfter(null, dataStart);

                // Adjust spacing since it's a bit different in the subid table
                dataStart.SetSpacing(0, "\t");
                dataStart.SetSpacing(1, "");

                // Insert newline after the new subid table
                parser.InsertParseableTextAfter(null, new string[] { "" });

                // Update dataStart (since the old data was moved)
                DetermineDataStart();
            }


            // Pointer either existed already or was just created. Insert new subid's data.
            Data lastSubidData = Project.GetData(dataStart.GetValue(0));
            TraverseSubidData(ref lastSubidData, NumTreasureObjectSubids - 1);

            dataStart.FileParser.InsertParseableTextAfter(lastSubidData,
                    new string[] { ConstructTreasureSubidString(NumTreasureObjectSubids, true) });
            return GetTreasureObject(NumTreasureObjectSubids - 1);
        }


        Data GetSubidBaseData(int subid) {
            Data data = dataStart;

            if (!UsesPointer) {
                if (subid == 0)
                    return data;
                else
                    return null;
            }

            // Uses pointer

            try {
                data = Project.GetData(data.GetValue(0)); // Follow pointer
            }
            catch (InvalidLookupException) {
                // Sometimes there is no pointer even when the "pointer" bit is set.
                return null;
            }
            if (TraverseSubidData(ref data, subid) != subid)
                return null;

            return data;
        }

        /// Traverses subid data up to "subid", returns the total number of subids actually
        /// traversed (there may be less than requested).
        /// Labels are considered to end a sequence of subid data.
        int TraverseSubidData(ref Data data, int subid, Action<FileComponent> action = null) {
            int count = 0;
            while (count < subid) {
                FileComponent com = data;
                if (action != null)
                    action(com);
                if (com is Label || com == null)
                    return count;
                com = com.Next;
                data = com as Data;
                count++;
            }
            return count;
        }

        void DetermineDataStart() {
            dataStart = Project.GetData("treasureObjectData", Index * 4);
        }
    }
}
