using System;
using System.IO;
using DPFP;
using DPFP.Capture;
using DPFP.Processing;
using DPFP.Verification;
using MySql.Data.MySqlClient;
using System.Collections.Generic;

namespace FingerprintVerification
{
    class Program : DPFP.Capture.EventHandler
    {
        private Capture Capturer;
        private bool matchCompleted = false;
        private List<Template> StoredTemplates = new List<Template>();  // List to store multiple templates
        private Dictionary<Template, string> TemplateUserNames = new Dictionary<Template, string>(); // Store templates with user names
        private Dictionary<Template, string> TemplateCourses = new Dictionary<Template, string>();
        private Dictionary<Template, string> Templatestudentid = new Dictionary<Template, string>();

        static void Main()
        {
            Program p = new Program();
            p.Init();

            // Load fingerprint templates from the database for all users
            p.LoadStoredTemplates();

            if (p.StoredTemplates.Count == 0)
            {
                Console.WriteLine("No fingerprint templates found.");
                Console.ReadLine();  // Wait for user input before exiting
                return;
            }

            Console.WriteLine("Place your finger on the scanner to verify...");

            while (!p.matchCompleted)
            {
                // Small delay to prevent high CPU usage
                System.Threading.Thread.Sleep(200);
            }

            // NEW: Exit automatically after successful scan
            Console.WriteLine("Attendance saved. Closing program...");
            Environment.Exit(0);
        }

        // Initialize scanner
        public void Init()
        {
            try
            {
                Capturer = new Capture();
                if (Capturer != null)
                {
                    Capturer.EventHandler = this;
                    Capturer.StartCapture();
                    Console.WriteLine("Fingerprint scanner initialized.");
                }
                else
                {
                    Console.WriteLine("Cannot initialize fingerprint scanner.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error initializing scanner: " + ex.Message);
            }
        }

        // Retrieve fingerprint templates and user information from DB (all users)
        public void LoadStoredTemplates()
        {
            try
            {
                string connStr = "Server=localhost;Database=library_db;Uid=root;Pwd=;";
                using (var conn = new MySqlConnection(connStr))
                {
                    conn.Open();
                    // Get all users' fingerprint templates and names
                    string sql = "SELECT fingerprint_template, fullname, yrcourse, studentid FROM users";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                byte[] templateBytes = reader["fingerprint_template"] as byte[];
                                string userName = reader["fullname"].ToString();  // Retrieve the user's name
                                string userCourse = reader["yrcourse"].ToString(); // Retrieve The user's course
                                string studentid = reader["studentid"].ToString(); // Retrieve The user's course

                                if (templateBytes != null)
                                {
                                    using (MemoryStream ms = new MemoryStream(templateBytes))
                                    {
                                        Template userTemplate = new Template(ms);
                                        StoredTemplates.Add(userTemplate);  // Add template to the list
                                        TemplateUserNames[userTemplate] = userName;  // Store corresponding username
                                        TemplateCourses[userTemplate] = userCourse;
                                        Templatestudentid[userTemplate] = studentid;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error retrieving templates: " + ex.Message);
            }
        }

        // Save attendance to the database
        public void SaveAttendanceToDB(string fullname, string course, string studentid)
        {
            try
            {
                string connStr = "Server=localhost;Database=library_db;Uid=root;Pwd=;";
                using (var conn = new MySqlConnection(connStr))
                {
                    conn.Open();
                    // Check database for timein of user (if meron, X)
                    string checktimein = @"SELECT id from attendance where fullname = @fullname AND date = CURDATE() AND timein IS NOT NULL AND timeout IS NULL LIMIT 1";

                    using (var check_for_timein = new MySqlCommand(checktimein, conn))
                    {
                        check_for_timein.Parameters.AddWithValue("@fullname", fullname);
                        var existingid = check_for_timein.ExecuteScalar();

                        if (existingid != null)
                        {
                            string updateSql = "UPDATE attendance SET timeout = CURTIME() WHERE id = @id";
                            using (var updateCmd = new MySqlCommand(updateSql, conn))
                            {
                                updateCmd.Parameters.AddWithValue("@id", existingid);
                                updateCmd.ExecuteNonQuery();
                            }

                            Console.WriteLine("Timeout recorded for " + fullname);
                        }
                        else
                        {
                            // Check database if complete na Both TIME IN AND TIME OUT
                            string checkcompleteattendance = @"SELECT id from attendance where fullname = @fullname AND date = CURDATE() AND timein IS NOT NULL AND timeout IS NOT NULL LIMIT 1";

                            using (var checkattedance = new MySqlCommand(checkcompleteattendance, conn))
                            {
                                checkattedance.Parameters.AddWithValue("@fullname", fullname);
                                var completed_id_attedance = checkattedance.ExecuteScalar();

                                // If completed -> Create new attendance for same user :D
                                string sql = "INSERT INTO attendance (studentid ,fullname, course, date, timein) VALUES (@studentid, @fullname, @course, CURDATE(), CURTIME())";
                                using (var cmd = new MySqlCommand(sql, conn))
                                {
                                    cmd.Parameters.AddWithValue("@fullname", fullname);
                                    cmd.Parameters.AddWithValue("@course", course);
                                    cmd.Parameters.AddWithValue("@studentid", studentid);
                                    cmd.ExecuteNonQuery();
                                }
                                Console.WriteLine("New time-in recorded for " + fullname);
                            }
                        }
                    }
                }
                matchCompleted = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error saving attendance: " + ex.Message);
            }
        }


        // Event: Fingerprint captured
        public void OnComplete(object Capture, string ReaderSerialNumber, Sample Sample)
        {
            Console.WriteLine("Fingerprint captured.");

            // Extract features from live sample for verification
            FeatureExtraction extractor = new FeatureExtraction();
            FeatureSet liveFeatures = new FeatureSet();
            CaptureFeedback feedback = CaptureFeedback.None;

            extractor.CreateFeatureSet(Sample, DataPurpose.Verification, ref feedback, ref liveFeatures);

            if (feedback != CaptureFeedback.Good)
            {
                Console.WriteLine("Poor quality fingerprint. Try again.");
                return;
            }

            // Verify against each stored template
            Verification verif = new Verification();
            Verification.Result result = new Verification.Result();

            bool matchFound = false;

            foreach (var storedTemplate in StoredTemplates)
            {
                verif.Verify(liveFeatures, storedTemplate, ref result);

                if (result.Verified)
                {
                    /*Console.WriteLine($"Fingerprint matches! Verified with user: {TemplateUserNames[storedTemplate]}"); -- Print name instead of saving to db */

                    string matchedUser = TemplateUserNames[storedTemplate];
                    string matchedCourse = TemplateCourses[storedTemplate];
                    string matchedstudentid = Templatestudentid[storedTemplate];
                    Console.WriteLine($"Fingerprint matches! Verified with user: {matchedUser}, {matchedCourse}, {matchedstudentid}");
                    // Insert into attendance table
                    SaveAttendanceToDB(matchedUser, matchedCourse, matchedstudentid);

                    matchFound = true;
                    break; // Exit the loop once a match is found
                }
            }

            if (!matchFound)
            {
                Console.WriteLine("Fingerprint does NOT match any stored user.");
            }
        }

        // Event handlers for scanner
        public void OnFingerGone(object Capture, string ReaderSerialNumber)
            => Console.WriteLine("Finger removed.");

        public void OnFingerTouch(object Capture, string ReaderSerialNumber)
            => Console.WriteLine("Finger touched scanner.");

        public void OnReaderConnect(object Capture, string ReaderSerialNumber)
            => Console.WriteLine("Scanner connected.");

        public void OnReaderDisconnect(object Capture, string ReaderSerialNumber)
            => Console.WriteLine("Scanner disconnected.");

        public void OnSampleQuality(object Capture, string ReaderSerialNumber, CaptureFeedback CaptureFeedback)
            => Console.WriteLine("Sample quality: " + CaptureFeedback);
    }
}
