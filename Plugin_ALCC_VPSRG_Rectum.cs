using System;
using System.Xml;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace VMS.TPS
{
  public class Script

  {
    /// <summary>
    /// This is the class's constructor, it does nothing other than allowing instance creation
    /// </summary>
    public Script()
    {
        }
    
    //*********//********* To make it executable
    // coment lines
    //  public void Execute(ScriptContext context /*, System.Windows.Window window*/)
    //  {
    // and uncoment following

    /// <summary>
    /// Plugin_ALCC_VPSRG_Rectum runs as binary script on Eclipse. It requires a patient open on the context.
    /// Promts for course and plan selection (prepared for VicRaplidPlan Rectum) and produces an xml file
    /// with demografics, quality metrics and selected structures' dvhs of plan for ViC Rapid plan project.
    /// </summary>
    /// <param name="context"></param>
    public void Execute(ScriptContext context /*, System.Windows.Window window*/)
    {
            // TODO : Add here your code that is called when the script is launched from Eclipse

            //** Defining Patient
            Patient my_patient = context.Patient;
            PlanningItem my_plan;

            // Variables to be used on the selection forms
            SelectBox selectDiag;
            String selected;
            Structure this_strct;
            List<String> my_list = new List<string>();
            // Next: Tuple with string(label),structure(selected Strct) to export DVHs and metrics
            List<Tuple<String, Structure>> selected_structs = new List<Tuple<String, Structure>>(); 
            // Next: List of code, label, ALCC-ID for searching structures top export
            List<Tuple<String, String, String>> lst_struct_to_search = new List<Tuple<String, String, String>>();
            SelectOneStruct selectOneStruct;
            IEnumerable<Structure> set_of_structs;
            IEnumerable<Structure> partial_set_of_structs;
            
            String title;
            DoseValue Dose_PTV_Medium = new DoseValue(0, "Gy");
            DoseValue Dose_PTV_Low= new DoseValue(0, "Gy");
            
            // Output string
            // this string will get the text reporting for import on VPSRG_Anus_case-tracking_sheet.xlsm 
            String VPSRG_Rectum_txt = null; // TODO change from VPSRG_HN_track **********************************!!!!!!!
            String Pat_Record_Id="";


            // "Metric" is a constant with a unit, even a "relative" unit (%)
            // "Goal" is a "Metric" that acts as an objective 
            // Metrics
            DoseValue Dose_Metric = new DoseValue(0, "Gy"); // absolute dose in Gy
            DoseValue Rel_Dose_Metric = new DoseValue(0.0, "%"); // relative dose in %
            double Vol_Metric = new double(); // absolute volume in cm3
            double Rel_Vol_Metric = new double(); // relative volume in %
            // Goals
            DoseValue Dose_Goal = new DoseValue(0, "Gy"); // absolute dose in Gy
            DoseValue Rel_Dose_Goal = new DoseValue(0, "%"); // relative dose goal in %
            double Vol_Goal = new double(); // absolute volume in cm3
            double Rel_Vol_Goal = new double(); // relative volume goal in %

            // Diagnosis
            String Diagnosis = ""; 
            String Stage = ""; 
            String Nodes_involvement = "";

            // Timing
            int Num_of_Cropped_OARs=-1;
            int Num_of_Added_Objectives=-1;
            int Num_of_Added_HotCold_CtrlStr=-1;
            int Num_of_Optim_reruns=-1;

            // Miscelaneous
            String text = null; // a needed text container
            double value; // a needed double container
            DoseValue Abs_Dose = new DoseValue(0.0, "Gy");
            DoseValue Rel_Dose = new DoseValue(0.0, "%");
            bool flag; // a boolean for control
            double bin = 0.01; // for binning the DVHs, this values is for plans in Gy
            bool alcc_flag=(my_patient.Hospital.Id=="Barwon Health"); // a flag for using ALCC-Ids
            String Rapidplan_version;

            //** Change or preserve Patient Record UR
            Pat_Record_Id = Microsoft.VisualBasic.Interaction.InputBox("Type Fake UR ?"
                + System.Environment.NewLine + System.Environment.NewLine
                    + "(leave blank + [Ok] or [Cancel]" + System.Environment.NewLine + " for keeping Original UR )"
                    , "Fake or Original UR");
            if (Pat_Record_Id.Length==0)
            {
                Pat_Record_Id = my_patient.Id;
            }
            
            //** Select course
            my_list.Clear();
            foreach (Course course in my_patient.Courses)
            { my_list.Add(course.Id); }
            selectDiag = new SelectBox(my_list, "Course Id");
            selected = selectDiag.Get_Item();
            Course my_course = my_patient.Courses.Where(c => c.Id.Equals(selected)).First();

            //** Select plan (takes in charge PlanSetup or PlanSum)
            my_list.Clear();
            foreach (PlanSetup plan in my_course.PlanSetups)
            { my_list.Add(plan.Id); }
            foreach (PlanSum plan in my_course.PlanSums)
            { my_list.Add(plan.Id+" (plan sum)"); }
            selectDiag = new SelectBox(my_list, "Plan Id");
            selected = selectDiag.Get_Item();
            // PlanSum
            if (selected.Contains(" (plan sum)")) 
            {
                selected = selected.Replace(" (plan sum)", "");
                my_plan = my_course.PlanSums.Where(c => c.Id.Equals(selected)).First();
            }
            // PlanSetup
            else 
            {
                my_plan = my_course.PlanSetups.Where(c => c.Id.Equals(selected)).First();
            }

            //** Define Prescription Dose and Prescription Isodose num of fractions and MU
            double TotalPrescribedDose=0.0;
            double PrescribedPercentage=0.0;
            double mu = 0.0;
            int? NumberOfFractions = 0;
            String vmat = "VMAT";
            String Original_or_RapidPlan = "n/a";

            if (my_plan is PlanSetup)
            {
                if (((PlanSetup) my_plan).TotalPrescribedDose.UnitAsString=="Gy")
                { TotalPrescribedDose = ((PlanSetup)my_plan).TotalPrescribedDose.Dose;}
                else
                { TotalPrescribedDose = ((PlanSetup)my_plan).TotalPrescribedDose.Dose/100.0;
                  bin = 1; }

                PrescribedPercentage = ((PlanSetup)my_plan).PrescribedPercentage*100.0; // To have that in %
                // Calc total MUs
                foreach (Beam beam in ((PlanSetup)my_plan).Beams) 
                { // folowing if for not getting NaN from setup beams
                    if (!Double.IsNaN(beam.Meterset.Value))
                    { mu = mu + beam.Meterset.Value; }
                }

                NumberOfFractions = ((PlanSetup)my_plan).UniqueFractionation.NumberOfFractions;

                //IMRT or VMAT, following Col P cannot be populated, skipping Col Q
                foreach (Beam b in ((PlanSetup)my_plan).Beams)
                {
                    if (!Double.IsNaN(b.Meterset.Value) && !(b.MLCPlanType.ToString() == "VMAT"))
                    {
                        vmat = "IMRT";
                    }
                }
            }
            else // PlanSum case
            {
                // Prescribed Dose - this while ensures tryparse succed
                while (!double.TryParse(
                        Microsoft.VisualBasic.Interaction.InputBox("Prescribed Dose [Gy]?", "Dprescr"), out value)
                      ) ;
                TotalPrescribedDose = value;
                // Prescribed Percentage (prescr. isodose as %) - this while ensures tryparse succed
                while (!double.TryParse(
                        Microsoft.VisualBasic.Interaction.InputBox("Prescribed Percentage [%]?", "% Prescription Isodose"), out value)
                      ) ;
                PrescribedPercentage = value;
                // MUs - this while ensures tryparse succed
                while (!double.TryParse(
                        Microsoft.VisualBasic.Interaction.InputBox("Total MUs (all beams) ?", "# of MUs"), out value)
                      ) ;
                mu = value;
                // NumberOfFractions - this while ensures tryparse succed
                Int32 value_int;
                while (!Int32.TryParse(
                        Microsoft.VisualBasic.Interaction.InputBox("Number Of fractions ?", "# of fractions"), out value_int)
                      ) ;
                NumberOfFractions = value_int;
                // Plan type (VMAT or IMRT)
                my_list.Clear();
                { my_list.Add("VMAT ?"); my_list.Add("IMRT ?"); }
                selectDiag = new SelectBox(my_list, "Select Plan Type");
                vmat = selectDiag.Get_Item().Replace(" ?","");
            }
            // Original_or_RapidPlan?
            my_list.Clear();
            { my_list.Add("Original ?"); my_list.Add("RapidPlan ?"); }
            selectDiag = new SelectBox(my_list, "Original Or RapidPlan?");
            Original_or_RapidPlan = selectDiag.Get_Item().Replace(" ?", "");
            

            //** Define number of PTVs
            my_list.Clear();
            my_list.Add("1 (PTV High Only)"); my_list.Add("2 (PTV High and Low)"); my_list.Add("3 (High, Med. and Low)");
            selectDiag = new SelectBox(my_list, "# of PTVs' Dose Levels");
            selected = selectDiag.Get_Item().First().ToString();
            int num_of_ptvs = Int32.Parse(selected);

            // Define dose levels of Int and Low PTVs
            if (num_of_ptvs==2) // Only Low
            {
                // this while ensures tryparse succed, thus double number entered as text
                while ( !double.TryParse(
                        Microsoft.VisualBasic.Interaction.InputBox("PTV LOW - Dose Level [Gy]?", "Low Dose Level"),
                        out value)
                      );
                Dose_PTV_Low = new DoseValue(value,"Gy");
            }
            if (num_of_ptvs == 3) // Medium and Low
            {
                // Medium - this while ensures tryparse succed, thus double number entered as text
                while (!double.TryParse(
                        Microsoft.VisualBasic.Interaction.InputBox("PTV MEDIUM - Dose Level [Gy]?", "MEDIUM Dose Level"),
                        out value)
                      ) ;
                Dose_PTV_Medium = new DoseValue(value, "Gy");

                // Low - this while ensures tryparse succed, thus double number entered as text
                while (!double.TryParse(
                        Microsoft.VisualBasic.Interaction.InputBox("PTV LOW - Dose Level [Gy]?", "Low Dose Level"),
                        out value)
                      ) ;
                Dose_PTV_Low = new DoseValue(value, "Gy");
            }

            //** Diagnosis
            Diagnosis = Microsoft.VisualBasic.Interaction.InputBox("Diagnosis ?"
                + System.Environment.NewLine + System.Environment.NewLine
                    + "(leave blank + [Ok] Or [Cancel] for skipping)", "Diagnosis");
            Stage = Microsoft.VisualBasic.Interaction.InputBox("Stage ?"
                + System.Environment.NewLine + System.Environment.NewLine
                    + "(leave blank + [Ok] Or [Cancel] for skipping)", "Stage");
            my_list.Clear();
            my_list.Add("Primary site only");
            my_list.Add("Primary site + Pelvic nodes");
            selectDiag = new SelectBox(my_list, "Pelvic nodes involvement");
            selected = selectDiag.Get_Item();
            if (selected == "Primary site only")
            { Nodes_involvement = "No"; }
            else
            { Nodes_involvement = "Yes"; }

            //** Timing statistics
            flag = true;
            while (flag)
            {
                text = Microsoft.VisualBasic.Interaction.InputBox("Number of cropped OARs ?"
                    + System.Environment.NewLine + System.Environment.NewLine
                    + "(leave blank + [Ok] Or [Cancel] for skipping)", "# of Cropped OARs");
                if (text == "")
                {
                    Num_of_Cropped_OARs = -1;
                    flag = false;
                }
                else
                {
                    if (int.TryParse(text, out int num))
                    {
                        Num_of_Cropped_OARs = num;
                        flag = false;
                    }
                }
            }
            flag = true;
            while (flag)
            {
                text = Microsoft.VisualBasic.Interaction.InputBox("Number of manually added objectives ?"
                    + System.Environment.NewLine + System.Environment.NewLine
                    + "(leave blank + [Ok] Or [Cancel] for skipping)", "# of Added Objectives");
                if (text == "")
                {
                    Num_of_Added_Objectives = -1;
                    flag = false;
                }
                else
                {
                    if (int.TryParse(text, out int num))
                    {
                        Num_of_Added_Objectives = num;
                        flag = false;
                    }
                }
            }
            flag = true;
            while (flag)
            {
                text = Microsoft.VisualBasic.Interaction.InputBox("Number of added Hot/Cold-spot control structures ?"
                    + System.Environment.NewLine + System.Environment.NewLine
                    + "(leave blank + [Ok] Or [Cancel] for skipping)", "# of Added Hot/Cold control Structures");
                if (text == "")
                {
                    Num_of_Added_HotCold_CtrlStr = -1;
                    flag = false;
                }
                else
                {
                    if (int.TryParse(text, out int num))
                    {
                        Num_of_Added_HotCold_CtrlStr = num;
                        flag = false;
                    }
                }
            }
            flag = true;
            while (flag)
            {
                text = Microsoft.VisualBasic.Interaction.InputBox("Number of optimization re-runs ?"
                    + System.Environment.NewLine + System.Environment.NewLine
                    + "(leave blank + [Ok] Or [Cancel] for skipping)", "# of Optimizer re-runs");
                if (text == "")
                {
                    Num_of_Optim_reruns = -1;
                    flag = false;
                }
                else
                {
                    if (int.TryParse(text, out int num))
                    {
                        Num_of_Optim_reruns = num;
                        flag = false;
                    }
                }
            }

            // RapidPlan Model version
            Rapidplan_version = Microsoft.VisualBasic.Interaction.InputBox("RapidPlan Model version used ?"
                    + System.Environment.NewLine + System.Environment.NewLine
                    + "(leave blank + [Ok] Or [Cancel] for skipping)", "RapidPlan Model version");

            //** Select all structures of interest First by code, then by heuristic, then prompt for name
            // alcc_flag=(HospID==Barwon Health) is used for searching first by ALCC Ids
            // Build the list of structures to search: (CODE, Label, ALCC-Id)
            // lst_struct_to_search.Add(Tuple.Create("BODY", "Body", "BODY")); BODY considered apart for not distracting
            // there is only one struct with dicom type "external"
            lst_struct_to_search.Add(Tuple.Create("15900", "Bladder", "Bladder"));
            lst_struct_to_search.Add(Tuple.Create("7200", "Bowel Small", "Bowel Small"));
            lst_struct_to_search.Add(Tuple.Create("7201", "Bowel Large", "Bowel Large"));
            lst_struct_to_search.Add(Tuple.Create("32843", "Femoral Head and Neck Left", "Femur (L)"));
            lst_struct_to_search.Add(Tuple.Create("32842", "Femoral Head and Neck Right", "Femur (R)"));
            lst_struct_to_search.Add(Tuple.Create("16591", "Left ilium", "Iliac Crest (L)"));
            lst_struct_to_search.Add(Tuple.Create("16590", "Right ilium", "Iliac Crest (R)"));
            lst_struct_to_search.Add(Tuple.Create("45643", "Genitalia External", "Ext Gen"));
            lst_struct_to_search.Add(Tuple.Create("15703", "Anal Canal", "Anal Canal"));
            lst_struct_to_search.Add(Tuple.Create("PTV_High", "PTV High Risk", "PTV 54")); // PTV High
            if (num_of_ptvs == 2) // PTV High + PTV Low ONLY
            { lst_struct_to_search.Add(Tuple.Create("PTV_Low", "PTV Low Risk", "IP PTV 45")); } 
            if (num_of_ptvs == 3) // PTV High + PTV Int + PTV Low
            { lst_struct_to_search.Add(Tuple.Create("PTV_Intermediate", "PTV Intermediate Risk", "IP PTV 50.4"));
              lst_struct_to_search.Add(Tuple.Create("PTV_Low", "PTV Low Risk", "IP PTV 45"));
            } 

            // **** Start searching for structures
            
            // First define the collection of strcutures
            if (my_plan is PlanSetup)
            {
                set_of_structs = ((PlanSetup) my_plan).StructureSet.Structures;
            }
            else
            {
                set_of_structs = ((PlanSum) my_plan).StructureSet.Structures;
            }

            // Search for body
            selected_structs.Add(Tuple.Create("Body",
                                set_of_structs.Where(s => s.DicomType.ToLower() == "external").First()));

            // Loop on list of structs to search
            foreach (Tuple<String,String,String> t in lst_struct_to_search)
            {
                flag = true; // to decide if promt user for name or not

                // A case apart for PTV High (2 possible codes: PTV_High or PTVp)
                if (t.Item1=="PTV_High") // time to search for 2 possible CODES: PTV_High and PTVp
                {
                    // Search by Contains(ALCC-Id) (can be more than 1) and NOT empty
                    if (alcc_flag && set_of_structs.Where(s => s.Id.Contains(t.Item3) && !s.IsEmpty).Any())
                    {
                        // check if only 1 has same code and is not empty
                        if (set_of_structs.Where(s => s.Id.Contains(t.Item3) && !s.IsEmpty).Count() == 1)
                        {
                            selected_structs.Add(Tuple.Create(t.Item2,
                                set_of_structs.Where(s => s.Id.Contains(t.Item3) && !s.IsEmpty).First()));
                            flag = false;
                        }
                        else // more than 1 then prompt for user choosing between non-empty ones
                        {
                            partial_set_of_structs = set_of_structs.Where(s => s.Id.Contains(t.Item3) && !s.IsEmpty);
                            title = t.Item2;
                            selectOneStruct = new SelectOneStruct(title, my_plan, partial_set_of_structs);
                            selected_structs.Add(Tuple.Create(t.Item2, selectOneStruct.Get_Selected()));
                            flag = false;
                        }
                    }
                    // Promt for name (if flag still true)
                    while (flag) // do while flag is true
                    {
                        text = Microsoft.VisualBasic.Interaction.InputBox("PTV High ? " + Environment.NewLine +
                            "(*) entire or partial name (case insensitive)" + Environment.NewLine , "Enter PTV High structure name");
                        // Have to evaluate: if text corresponds to structure, select structure and set flag false to cont.
                        //                   if text not correspond to structure, keep flag=true to ask again
                        if (text != "") // non empty string
                        {
                            if (set_of_structs.Where(s => s.Id.ToLower().Contains(text.ToLower()) && !s.IsEmpty).Any()) //Found flag to false
                            {
                                // check exact match and is not empty
                                if (set_of_structs.Where(s => s.Id == text && !s.IsEmpty).Any())
                                {
                                    selected_structs.Add(Tuple.Create(t.Item2,
                                        set_of_structs.Where(s => s.Id == text && !s.IsEmpty).First()));
                                    flag = false;
                                }
                                else // more than 1 then prompt for user choosing between non-empty ones
                                {
                                    partial_set_of_structs = set_of_structs.Where(s => s.Id.ToLower().Contains(text.ToLower()) 
                                                                                        && !s.IsEmpty);
                                    title = t.Item2;
                                    selectOneStruct = new SelectOneStruct(title, my_plan, partial_set_of_structs);
                                    selected_structs.Add(Tuple.Create(t.Item2, selectOneStruct.Get_Selected()));
                                    flag = false;
                                }
                            }
                            else // not found or empty
                            {
                                if (!set_of_structs.Where(s => s.Id.ToLower() == text.ToLower()).Any())
                                { // Mesage if not any found checking with lower case (any case)
                                    System.Windows.MessageBox.Show("There is no structure with" + System.Environment.NewLine +
                                          "Id containing: " + text);
                                }
                                else
                                { // found but empty
                                    System.Windows.MessageBox.Show("Structure with Id:" + System.Environment.NewLine +
                                           text + "  is empty.");
                                }
                            }
                        }
                    }
                }
                else // t points to any other structure not PTV_High
                {
                    // Search by ALCC-Id
                    if (alcc_flag && set_of_structs.Where(s => s.Id.Contains(t.Item3) && !s.IsEmpty).Any())
                    {
                        // check if only 1 has same code and is not empty
                        if (set_of_structs.Where(s => s.Id.Contains(t.Item3) && !s.IsEmpty).Count() == 1)
                        {
                            selected_structs.Add(Tuple.Create(t.Item2,
                                set_of_structs.Where(s => s.Id.Contains(t.Item3) && !s.IsEmpty).First()));
                            flag = false;
                        }
                        else // more than 1 then prompt for user choosing between non-empty ones
                        {
                            partial_set_of_structs = set_of_structs.Where(s => s.Id.Contains(t.Item3) && !s.IsEmpty);
                            title = t.Item2;
                            selectOneStruct = new SelectOneStruct(title, my_plan, partial_set_of_structs);
                            selected_structs.Add(Tuple.Create(t.Item2, selectOneStruct.Get_Selected()));
                            flag = false;
                        }
                    }
                    // Promt for name (if flag still true)
                    while (flag) // do while flag is true
                    {
                        text = Microsoft.VisualBasic.Interaction.InputBox(t.Item2 + "?" + Environment.NewLine +
                            "(*) entire or partial name (case insensitive)" + Environment.NewLine + 
                            "( leave empty or cancel for not selecting any )", "Enter structure name");
                        // Have to evaluete: if text empty, then don't choose nothing ans set flag false to continue
                        //                   if text corresponds to structure, select structure and set flag false to cont.
                        //                   if text not correspond to structure, keep flag=true to ask again
                        if (text != "") // non empty string
                        {
                            if (set_of_structs.Where(s => s.Id.ToLower().Contains(text.ToLower()) && !s.IsEmpty).Any()) // Found match flag to false
                            {
                                // check exact match and is not empty
                                if (set_of_structs.Where(s => s.Id == text && !s.IsEmpty).Any())
                                {
                                    selected_structs.Add(Tuple.Create(t.Item2,
                                        set_of_structs.Where(s => s.Id == text && !s.IsEmpty).First()));
                                    flag = false;
                                }
                                else // more than 1 then prompt for user choosing between non-empty ones
                                {
                                    partial_set_of_structs = set_of_structs.Where(s => s.Id.ToLower().Contains(text.ToLower()) 
                                                                                       && !s.IsEmpty);
                                    title = t.Item2;
                                    selectOneStruct = new SelectOneStruct(title, my_plan, partial_set_of_structs);
                                    selected_structs.Add(Tuple.Create(t.Item2, selectOneStruct.Get_Selected()));
                                    flag = false;
                                }
                            }
                        }
                        else
                        {
                            if (!(t.Item1=="PTV_Low") && !(t.Item1=="PTV_Intermediate")) // Case of not PTV_low nor PTV_Interm.
                            { flag = false; } // empty string canceling imput but not for PTV Low/Intermediate
                        }
                    }
                }// cont. loop on list of structs to search
            } // End of loop on list of strcuts to search

            // Now I got all the structures I need

            //** First, get all the DVHs in abs dose, abs vol and buil the xml file
            // from \\bhisilon-cifs.swarh.net\userdata\FNELLI\ESAPI_projects\Tips and trics\Extract XML data\
            // Extracts the full ESAPI XML.pdf

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = ("\t")
            };
            System.IO.MemoryStream mStream = new System.IO.MemoryStream();
            using (XmlWriter writer = XmlWriter.Create(mStream, settings))
            {
                writer.WriteStartDocument(true);
                writer.WriteStartElement("VICRP_Rectum"); // Root element
                    writer.WriteStartElement("Patient_Id");
                    writer.WriteString(Pat_Record_Id);
                    writer.WriteEndElement(); // </Patient_Id>
                    writer.WriteStartElement("Hospital");
                    writer.WriteString(my_patient.Hospital.Id);
                    writer.WriteEndElement(); // </Hospital>
                    writer.WriteStartElement("Plan_ID");
                    writer.WriteString(my_plan.Id);
                    writer.WriteEndElement(); // </Plan_Id>
                    writer.WriteStartElement("Original_or_RapidPlan");
                    writer.WriteString(Original_or_RapidPlan);
                    writer.WriteEndElement(); // </Original_or_RapidPlan>
                    writer.WriteStartElement("Diagnosis");
                    writer.WriteString(Diagnosis);
                    writer.WriteEndElement(); // </Diagnosis>
                    writer.WriteStartElement("Stage");
                    writer.WriteString(Stage);
                    writer.WriteEndElement(); // </Stage>
                    writer.WriteStartElement("Nodes_involvement");
                    writer.WriteString(Nodes_involvement);
                    writer.WriteEndElement(); // </Nodes_involvement>
                    writer.WriteStartElement("Timming");
                        writer.WriteStartElement("Num_of_Cropped_OARs");
                        writer.WriteString(Num_of_Cropped_OARs.ToString());
                        writer.WriteEndElement(); // </Plan_Id>
                        writer.WriteStartElement("Num_of_Added_Objectives");
                        writer.WriteString(Num_of_Added_Objectives.ToString());
                        writer.WriteEndElement(); // </Num_of_Added_Objectives>
                        writer.WriteStartElement("Num_of_Added_HotCold_CtrlStr");
                        writer.WriteString(Num_of_Added_HotCold_CtrlStr.ToString());
                        writer.WriteEndElement(); // </Num_of_Added_HotCold_CtrlStr>
                        writer.WriteStartElement("Num_of_Optim_reruns");
                        writer.WriteString(Num_of_Optim_reruns.ToString());
                        writer.WriteEndElement(); // </Num_of_Optim_reruns>
                    writer.WriteEndElement(); // </Timing>
                    writer.WriteStartElement("Total_Dose");
                    writer.WriteString(Math.Round(TotalPrescribedDose, 3).ToString());
                    writer.WriteEndElement(); // </Total_Dose>
                    writer.WriteStartElement("Prescribed_Percentage");
                    writer.WriteString(Math.Round(PrescribedPercentage, 3).ToString());
                    writer.WriteEndElement(); // </Prescribed_Percentage>
                    writer.WriteStartElement("NumberOfFractions");
                    writer.WriteString(NumberOfFractions.ToString());
                    writer.WriteEndElement(); // </NumberOfFractions>
                    writer.WriteStartElement("MU_total");
                    writer.WriteString(Math.Round(mu, 3).ToString());
                    writer.WriteEndElement(); // </MU_total>
                    writer.WriteStartElement("PlanType");
                    writer.WriteString(vmat);
                    writer.WriteEndElement(); // </PlanType>
                    writer.WriteStartElement("RapidPlanModelVersion");
                    writer.WriteString(Rapidplan_version);
                    writer.WriteEndElement(); // </RapidPlanModelVersion>
                    // loop on DVHs
                    foreach (var SelStructrTuple in selected_structs)
                    {
                      DVHData dvh = my_plan.GetDVHCumulativeData(SelStructrTuple.Item2,
                             DoseValuePresentation.Absolute, VolumePresentation.AbsoluteCm3, bin);
                      writer.WriteStartElement("DVHData");
                        writer.WriteStartElement("DVH_Label");
                        writer.WriteString(SelStructrTuple.Item1);
                        writer.WriteEndElement(); // </DVH_Label>
                        writer.WriteStartElement("SamplingCoverage");
                        writer.WriteString(Math.Round(dvh.SamplingCoverage,5).ToString());
                        writer.WriteEndElement(); // </SamplingCoverage>
                        writer.WriteStartElement("Str_Volume");
                        writer.WriteString(Math.Round(dvh.Volume,3).ToString());
                        writer.WriteEndElement(); // </Str_Volume>
                        writer.WriteStartElement("Vol_Unit");
                        writer.WriteString("cm3");
                        writer.WriteEndElement(); // </Vol_Unit>
                        writer.WriteStartElement("Dose_Unit");
                        writer.WriteString(dvh.MaxDose.UnitAsString);
                        writer.WriteEndElement(); // </Dose_Unit>
                        writer.WriteStartElement("Curve_Data");
                            foreach (var point in dvh.CurveData)
                            {
                                writer.WriteStartElement("DVH_point");
                                    writer.WriteStartElement("Dose");
                                    writer.WriteString(Math.Round(point.DoseValue.Dose,4).ToString());
                                    writer.WriteEndElement(); // </Dose>
                                    writer.WriteStartElement("Volume");
                                    writer.WriteString(Math.Round(point.Volume,3).ToString());
                                    writer.WriteEndElement(); // </Volume>
                                writer.WriteEndElement(); // </DVH_point>
                            }
                        writer.WriteEndElement(); // </Curve_Data>
                      writer.WriteEndElement(); // </DVHData>

                        //Check if Estimated DVH exist and write data if yes
                        if (my_plan is PlanSetup)
                        {
                            EstimatedDVH upEstDVH = null;
                            EstimatedDVH lowEstDVH = null;
                            if (((PlanSetup)my_plan).DVHEstimates.
                                Where(s => s.Structure.Id == SelStructrTuple.Item2.Id && s.Type == DVHEstimateType.Upper).Any())
                            {
                                upEstDVH = ((PlanSetup)my_plan).DVHEstimates.
                                First(s => s.Structure.Id == SelStructrTuple.Item2.Id && s.Type == DVHEstimateType.Upper);
                            }

                            if (((PlanSetup)my_plan).DVHEstimates.
                                Where(s => s.Structure.Id == SelStructrTuple.Item2.Id && s.Type == DVHEstimateType.Lower).Any())
                            {
                                lowEstDVH = ((PlanSetup)my_plan).DVHEstimates.
                                First(s => s.Structure.Id == SelStructrTuple.Item2.Id && s.Type == DVHEstimateType.Lower);
                            }

                            if ((upEstDVH != null && lowEstDVH != null)) // REVISAR !!!!!!!!!!!!!!!!!!!!!
                            {
                                // First calc scale convertion for DVH to Abs Dose and Abs Volume
                                double doseScale = 1;
                                string doseUnit = "Gy";
                                double volScale = 1;

                                //LowEstimate
                                if ((int)lowEstDVH.CurveData[0].DoseValue.Unit == 3)
                                {
                                    // doses in % (of prescriptio) to be converted into abs dose
                                    // preserving the global units of the plan
                                    doseScale = TotalPrescribedDose / PrescribedPercentage;
                                    doseUnit = lowEstDVH.CurveData[0].DoseValue.Unit.ToString();
                                }
                                if (lowEstDVH.CurveData[0].VolumeUnit == "%")
                                {
                                    // volumes in % (of Struct. Volume) to be converted into abs volume [cm3]
                                    volScale = SelStructrTuple.Item2.Volume / 100;
                                }

                                //Start writing lower estimate
                                writer.WriteStartElement("EstimatedDVH");
                                writer.WriteStartElement("EstimatedDVH_Label");
                                writer.WriteString(SelStructrTuple.Item1);
                                writer.WriteEndElement(); // </EstimatedDVH_Label>
                                writer.WriteStartElement("EstimatedDVH_Type");
                                writer.WriteString(lowEstDVH.Type.ToString());
                                writer.WriteEndElement(); // </EstimatedDVH_Type>
                                writer.WriteStartElement("Str_Volume");
                                writer.WriteString(Math.Round(SelStructrTuple.Item2.Volume, 3).ToString());
                                writer.WriteEndElement(); // </Str_Volume>
                                writer.WriteStartElement("Vol_Unit");
                                writer.WriteString("cm3");
                                writer.WriteEndElement(); // </Vol_Unit>
                                writer.WriteStartElement("Dose_Unit");
                                writer.WriteString(doseUnit);
                                writer.WriteEndElement(); // </Dose_Unit>
                                writer.WriteStartElement("Curve_Data");
                                foreach (var point in lowEstDVH.CurveData)
                                {
                                    writer.WriteStartElement("DVH_point");
                                    writer.WriteStartElement("Dose");
                                    writer.WriteString(Math.Round(point.DoseValue.Dose * doseScale, 4).ToString());
                                    writer.WriteEndElement(); // </Dose>
                                    writer.WriteStartElement("Volume");
                                    writer.WriteString(Math.Round(point.Volume * volScale, 3).ToString());
                                    writer.WriteEndElement(); // </Volume>
                                    writer.WriteEndElement(); // </DVH_point>
                                }
                                writer.WriteEndElement(); // </Curve_Data>
                                writer.WriteEndElement(); // </EstimatedDVH>

                                //UpEstimate
                                if ((int)upEstDVH.CurveData[0].DoseValue.Unit == 3)
                                {
                                    // doses in % (of prescriptio) to be converted into abs dose
                                    // preserving the global units of the plan
                                    doseScale = TotalPrescribedDose / PrescribedPercentage;
                                    doseUnit = upEstDVH.CurveData[0].DoseValue.Unit.ToString();
                                }
                                if (upEstDVH.CurveData[0].VolumeUnit == "%")
                                {
                                    // volumes in % (of Struct. Volume) to be converted into abs volume [cm3]
                                    volScale = SelStructrTuple.Item2.Volume / 100;
                                }

                                //Start writing uper estimate
                                writer.WriteStartElement("EstimatedDVH");
                                writer.WriteStartElement("EstimatedDVH_Label");
                                writer.WriteString(SelStructrTuple.Item1);
                                writer.WriteEndElement(); // </EstimatedDVH_Label>
                                writer.WriteStartElement("EstimatedDVH_Type");
                                writer.WriteString(upEstDVH.Type.ToString());
                                writer.WriteEndElement(); // </EstimatedDVH_Type>
                                writer.WriteStartElement("Str_Volume");
                                writer.WriteString(Math.Round(SelStructrTuple.Item2.Volume, 3).ToString());
                                writer.WriteEndElement(); // </Str_Volume>
                                writer.WriteStartElement("Vol_Unit");
                                writer.WriteString("cm3");
                                writer.WriteEndElement(); // </Vol_Unit>
                                writer.WriteStartElement("Dose_Unit");
                                writer.WriteString(doseUnit);
                                writer.WriteEndElement(); // </Dose_Unit>
                                writer.WriteStartElement("Curve_Data");
                                foreach (var point in upEstDVH.CurveData)
                                {
                                    writer.WriteStartElement("DVH_point");
                                    writer.WriteStartElement("Dose");
                                    writer.WriteString(Math.Round(point.DoseValue.Dose * doseScale, 4).ToString());
                                    writer.WriteEndElement(); // </Dose>
                                    writer.WriteStartElement("Volume");
                                    writer.WriteString(Math.Round(point.Volume * volScale, 3).ToString());
                                    writer.WriteEndElement(); // </Volume>
                                    writer.WriteEndElement(); // </DVH_point>
                                }
                                writer.WriteEndElement(); // </Curve_Data>
                                writer.WriteEndElement(); // </EstimatedDVH>
                            }
                        }
                    }
                    writer.WriteEndDocument();
                    // done writing to the memory stream
                    writer.Flush();
                    mStream.Flush();


                // create the XML file.
                // This version 1.4 gives user option to choose folder where to write output
                //
                FolderBrowserDialog fbd = new FolderBrowserDialog();
                fbd.RootFolder = Environment.SpecialFolder.MyComputer;
                fbd.SelectedPath = @"C:\temp";
                fbd.Description = "Select destination folder" + System.Environment.NewLine
                    + System.Environment.NewLine + @"if 'Cancel' or closed [x] -> C:\temp gets selected"
                    + System.Environment.NewLine;
                DialogResult usr_selected = fbd.ShowDialog();

                string temp = fbd.SelectedPath;
                    text = temp + @"\" + my_patient.Hospital.Id + @"\" + Pat_Record_Id;
                    System.IO.Directory.CreateDirectory(text);
                    string sXMLPath = text + @"\" + my_patient.Hospital.Id + "_" + Pat_Record_Id + "_" + my_plan.Id + ".xml";

                    using (System.IO.FileStream file = new System.IO.FileStream(sXMLPath,
                    System.IO.FileMode.Create, System.IO.FileAccess.Write))
                    {
                        // Have to rewind the MemoryStream in order to read its contents.
                        mStream.Position = 0;
                        mStream.CopyTo(file);
                        file.Flush();
                        file.Close();
                    }

                    // 'Start' generated XML file to launch browser window
                        // System.Diagnostics.Process.Start(sXMLPath);
                    // Sleep for a few seconds to let internet browser window to start
                        // System.Threading.Thread.Sleep(TimeSpan.FromSeconds(3));
            }

            // Now the creation of the text file for the worksheet
            // Col A - Skip B
            VPSRG_Rectum_txt = Original_or_RapidPlan + ", , "; // Skip B
            // Col C 
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + my_patient.Hospital.Id + ", ";
            // Col D 
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + Pat_Record_Id + ", "; 
            // Col E
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + Diagnosis +", ";
            // Col F
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + Stage + ", ";
            // Col G - Skip H
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + Nodes_involvement + ", , "; // Skip H 
            // Col I
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + Num_of_Cropped_OARs.ToString() + ", ";
            // Col J
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + Num_of_Added_Objectives.ToString() + ", ";
            // Col K
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + Num_of_Added_HotCold_CtrlStr.ToString() + ", ";
            // Col L - Skip M
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + Num_of_Optim_reruns.ToString() + ", , "; // Skip M
            // Col N
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(TotalPrescribedDose, 2).ToString() + ", ";
            // Col O
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(PrescribedPercentage,2).ToString() + ", ";
            // Col P
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(mu, 1).ToString() + ", ";
            // Col Q 
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + vmat + ", "; 
            // Col R - Skip R
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + Rapidplan_version + ", , "; // Skip S
            // Col T-W: Bladder
            if (selected_structs.Where(t => t.Item1 == "Bladder").Any())
            {
                this_strct = selected_structs.Where(t => t.Item1 == "Bladder").First().Item2;
                // Col T 
                Abs_Dose = new DoseValue(50, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col U 
                Abs_Dose = new DoseValue(40, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col V 
                Abs_Dose = new DoseValue(35, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col W 
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(this_strct.Volume, 2).ToString() + ", ";
            }
            else
            {
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", , , , "; // If not Bladder then Skip 4 Cols T-W 
            }
            // Col X-AB: Small Bowel
            if (selected_structs.Where(t => t.Item1 == "Bowel Small").Any())
            {
                this_strct = selected_structs.Where(t => t.Item1 == "Bowel Small").First().Item2;
                // Col X 
                Abs_Dose = new DoseValue(45, "Gy");
                Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.AbsoluteCm3);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Vol_Metric, 2).ToString() + ", ";
                // Col Y 
                Abs_Dose = new DoseValue(35, "Gy");
                Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.AbsoluteCm3);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Vol_Metric, 2).ToString() + ", ";
                // Col Z 
                Abs_Dose = new DoseValue(30, "Gy");
                Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.AbsoluteCm3);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Vol_Metric, 2).ToString() + ", ";
                // Col AA
                Rel_Vol_Metric = 0.0; // % 
                Dose_Metric = ALCC_QM.D_X_report(my_plan, this_strct,
                    Rel_Vol_Metric, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col AB 
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(this_strct.Volume, 2).ToString() + ", ";
            }
            else
            {
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", , , , , "; // If not Small Bowel then Skip 5 Cols X-AB 
            }
            // Col AC-AG: Large Bowel
            if (selected_structs.Where(t => t.Item1 == "Bowel Large").Any())
            {
                this_strct = selected_structs.Where(t => t.Item1 == "Bowel Large").First().Item2;
                // Col AC 
                Abs_Dose = new DoseValue(45, "Gy");
                Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.AbsoluteCm3);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Vol_Metric, 2).ToString() + ", ";
                // Col AD 
                Abs_Dose = new DoseValue(35, "Gy");
                Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.AbsoluteCm3);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Vol_Metric, 2).ToString() + ", ";
                // Col AE
                Abs_Dose = new DoseValue(30, "Gy");
                Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.AbsoluteCm3);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Vol_Metric, 2).ToString() + ", ";
                // Col AF
                Rel_Vol_Metric = 0.0; // % 
                Dose_Metric = ALCC_QM.D_X_report(my_plan, this_strct,
                    Rel_Vol_Metric, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col AG 
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(this_strct.Volume, 2).ToString() + ", ";
            }
            else
            {
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", , , , , "; // If not Large Bowel then Skip 5 Cols X-AB
            }
            // Col AH-AK: LT Femoral Head
            if (selected_structs.Where(t => t.Item1 == "Femoral Head and Neck Left").Any())
            {
                this_strct = selected_structs.Where(t => t.Item1 == "Femoral Head and Neck Left").First().Item2;
                // Col AH 
                Abs_Dose = new DoseValue(45, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col AI
                Abs_Dose = new DoseValue(40, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col AJ
                Abs_Dose = new DoseValue(30, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col AK
                Dose_Metric = ALCC_QM.Max_Dose(my_plan, this_strct, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
            }
            else
            {
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", , , , "; // If not Femoral H&N L then Skip 4 Cols AH-AK
            }
            // Col AL-AO: RT Femoral Head
            if (selected_structs.Where(t => t.Item1 == "Femoral Head and Neck Right").Any())
            {
                this_strct = selected_structs.Where(t => t.Item1 == "Femoral Head and Neck Right").First().Item2;
                // Col AL 
                Abs_Dose = new DoseValue(44, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col AM
                Abs_Dose = new DoseValue(40, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col AN
                Abs_Dose = new DoseValue(30, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col AO
                Dose_Metric = ALCC_QM.Max_Dose(my_plan, this_strct, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
            }
            else
            {
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", , , , "; // If not Femoral H&N R then Skip 4 Cols AL-AO
            }
            // Col AP-AR: Left ilium
            if (selected_structs.Where(t => t.Item1 == "Left ilium").Any())
            {
                this_strct = selected_structs.Where(t => t.Item1 == "Left ilium").First().Item2;
                // Col AP
                Abs_Dose = new DoseValue(50, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col AO
                Abs_Dose = new DoseValue(40, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col AP
                Abs_Dose = new DoseValue(30, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
            }
            else
            {
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", , , "; // If not Left ilium then Skip 3 Cols AP-AR
            }
            // Col AS-AU: Right ilium
            if (selected_structs.Where(t => t.Item1 == "Right ilium").Any())
            {
                this_strct = selected_structs.Where(t => t.Item1 == "Right ilium").First().Item2;
                // Col AR
                Abs_Dose = new DoseValue(50, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col AS
                Abs_Dose = new DoseValue(40, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col AU
                Abs_Dose = new DoseValue(30, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
            }
            else
            {
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", , , "; // If not Right ilium then Skip 3 Cols AS-AU
            }
            // Col AV-AY: Genitalia External
            if (selected_structs.Where(t => t.Item1 == "Genitalia External").Any())
            {
                this_strct = selected_structs.Where(t => t.Item1 == "Genitalia External").First().Item2;
                // Col AV
                Abs_Dose = new DoseValue(40, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col AW
                Abs_Dose = new DoseValue(30, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col AX - Skip AW
                Abs_Dose = new DoseValue(20, "Gy");
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", "; 
                // Col AY 
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(this_strct.Volume, 2).ToString() + ", ";
            }
            else
            {
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", , , , "; // If not Genitalia External then Skip 4 Cols AV-AY
            }
            // Col AZ-BA: Anal Canal - Skip BB
            if (selected_structs.Where(t => t.Item1 == "Anal Canal").Any())
            {
                this_strct = selected_structs.Where(t => t.Item1 == "Anal Canal").First().Item2;
                // Col AZ
                Rel_Vol_Metric = 5.0; // % Str_Vol
                Dose_Metric = ALCC_QM.D_X_report(my_plan, this_strct,
                    Rel_Vol_Metric, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BA 
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(this_strct.Volume, 2).ToString() + ", , ";// 2, for Skip BB 
            }
            else
            {
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", , , "; // If not Genitalia External then Skip 3 Cols AA-BB
            }
            //** PTVs
            // Col BC-BI: PTV High Risk
            if (selected_structs.Where(t => t.Item1 == "PTV High Risk").Any())
            {
                this_strct = selected_structs.Where(t => t.Item1 == "PTV High Risk").First().Item2;
                // Col BC
                Rel_Vol_Metric = 98.0; // % Str_Vol
                Dose_Metric = ALCC_QM.D_X_report(my_plan, this_strct,
                    Rel_Vol_Metric, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BD
                Rel_Vol_Metric = 2.0; // % Str_Vol
                Dose_Metric = ALCC_QM.D_X_report(my_plan, this_strct,
                    Rel_Vol_Metric, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BE
                Rel_Vol_Metric = 50.0; // % Str_Vol
                Dose_Metric = ALCC_QM.D_X_report(my_plan, this_strct,
                    Rel_Vol_Metric, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BF
                Abs_Dose = new DoseValue(0.99* TotalPrescribedDose, "Gy"); // (x)% of Prescribed dose
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col BG
                Abs_Dose = new DoseValue(0.95 * TotalPrescribedDose, "Gy"); // (x)% of Prescribed dose
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col BH
                Dose_Metric = ALCC_QM.Mean_Dose(my_plan, this_strct, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BI
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(this_strct.Volume, 2).ToString() + ", ";
            }
            else
            {
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", , , , , , , "; // If not PTV High Risk then Skip 7 Cols BC-BI
            }
            // Col BJ-BQ: PTV Intermediate Risk
            if (selected_structs.Where(t => t.Item1 == "PTV Intermediate Risk").Any())
            {
                this_strct = selected_structs.Where(t => t.Item1 == "PTV Intermediate Risk").First().Item2;
                // Col BJ
                Rel_Vol_Metric = 98.0; // % Str_Vol
                Dose_Metric = ALCC_QM.D_X_report(my_plan, this_strct,
                    Rel_Vol_Metric, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BK
                Rel_Vol_Metric = 2.0; // % Str_Vol
                Dose_Metric = ALCC_QM.D_X_report(my_plan, this_strct,
                    Rel_Vol_Metric, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BL
                Rel_Vol_Metric = 50.0; // % Str_Vol
                Dose_Metric = ALCC_QM.D_X_report(my_plan, this_strct,
                    Rel_Vol_Metric, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BM
                Abs_Dose = new DoseValue(0.99 * Dose_PTV_Medium.Dose, "Gy"); // (x)% of Prescribed dose PTV int
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col BN
                Abs_Dose = new DoseValue(0.95 * Dose_PTV_Medium.Dose, "Gy"); // (x)% of Prescribed dose PTV int
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col BO
                Dose_Metric = ALCC_QM.Mean_Dose(my_plan, this_strct, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BP
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(this_strct.Volume, 2).ToString() + ", ";
                // Col BQ
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_PTV_Medium.Dose, 2).ToString() + ", ";
            }
            else
            {
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", , , , , , , , "; // If not PTV Intermediate Risk then Skip 8 Cols BJ-BQ
            }
            // Col BR-BY: PTV Low Risk - Skip BZ
            if (selected_structs.Where(t => t.Item1 == "PTV Low Risk").Any())
            {
                this_strct = selected_structs.Where(t => t.Item1 == "PTV Low Risk").First().Item2;
                // Col BR
                Rel_Vol_Metric = 98.0; // % Str_Vol
                Dose_Metric = ALCC_QM.D_X_report(my_plan, this_strct,
                    Rel_Vol_Metric, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BS
                Rel_Vol_Metric = 2.0; // % Str_Vol
                Dose_Metric = ALCC_QM.D_X_report(my_plan, this_strct,
                    Rel_Vol_Metric, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BT
                Rel_Vol_Metric = 50.0; // % Str_Vol
                Dose_Metric = ALCC_QM.D_X_report(my_plan, this_strct,
                    Rel_Vol_Metric, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BU
                Abs_Dose = new DoseValue(0.99 * Dose_PTV_Low.Dose, "Gy"); // (x)% of Prescribed dose PTV Low
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col BV
                Abs_Dose = new DoseValue(0.95 * Dose_PTV_Low.Dose, "Gy"); // (x)% of Prescribed dose PTV Low
                Rel_Vol_Metric = ALCC_QM.V_X_report(my_plan, this_strct, Abs_Dose, VolumePresentation.Relative);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Rel_Vol_Metric, 2).ToString() + ", ";
                // Col BW
                Dose_Metric = ALCC_QM.Mean_Dose(my_plan, this_strct, DoseValuePresentation.Absolute);
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_Metric.Dose, 2).ToString() + ", ";
                // Col BX
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(this_strct.Volume, 2).ToString() + ", ";
                // Col BY
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + Math.Round(Dose_PTV_Low.Dose, 2).ToString() + ", , "; // Skip BZ
            }
            else
            {
                VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", , , , , , , , , "; // If not PTV Low Risk then Skip 9 Cols BR-BY
            }
            // Col CA
            VPSRG_Rectum_txt = VPSRG_Rectum_txt + num_of_ptvs.ToString() + ", , , ";// Skip CB-CC

            // Name of selected structures CD-CO
            foreach (var n in lst_struct_to_search)
            {
                if (selected_structs.Where(t => t.Item1 == n.Item2).Any())
                {
                    this_strct = selected_structs.Where(t => t.Item1 == n.Item2).First().Item2;
                    if (num_of_ptvs == 2 && n.Item2== "PTV High Risk") // PTV High + PTV Low ONLY
                    {
                        VPSRG_Rectum_txt = VPSRG_Rectum_txt + this_strct.Name + ", , ";
                    }
                    else
                    {
                        VPSRG_Rectum_txt = VPSRG_Rectum_txt + this_strct.Name + ", ";
                    }
                }
                else
                {
                    VPSRG_Rectum_txt = VPSRG_Rectum_txt + ", ";
                }

            }

            // Build output text
            String txt = "Target (Code : Label)" + "  -->  " + "Selected (Id)" + System.Environment.NewLine;
            txt = txt + "__________________________________________" + System.Environment.NewLine + System.Environment.NewLine;
            foreach (var item in selected_structs.Where(s => s.Item1 != "Body"))
            {
                txt = txt + lst_struct_to_search.Where(l => l.Item2== item.Item1).First().Item1 + " : " + item.Item1 
                            + "  -->  " + item.Item2.Id + System.Environment.NewLine;
            }
            System.Windows.MessageBox.Show(txt,"Selected structures");

            //** Done, now to write the text
            string txtPath = text + @"\" + my_patient.Hospital.Id + "_" + Pat_Record_Id + "_" + my_plan.Id + ".csv";
            string txtMsgPath = text + @"\SelectedStructures_" + my_patient.Hospital.Id + "_" 
                                    + Pat_Record_Id + "_" + my_plan.Id + ".txt";

            // create or overwrite
            System.IO.File.WriteAllText(txtMsgPath, txt, Encoding.UTF8);
            System.IO.File.WriteAllText(txtPath, VPSRG_Rectum_txt, Encoding.UTF8);

            System.Windows.MessageBox.Show("Files saved @" + System.Environment.NewLine + text);
            // System.Windows.Clipboard.Clear();
            // System.Windows.Clipboard.SetText(@"c:\temp\");

    }

  }
}
