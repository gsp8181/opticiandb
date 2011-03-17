﻿/*
 * Copyright (c) 2011 Geoffrey Prytherch
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this
 * software and associated documentation files (the "Software"), to deal in the Software
 * without restriction, including without limitation the rights to use, copy, modify, merge,
 * publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
 * to whom the Software is furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all copies or
 * substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
 * INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
 * PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
 * FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
 * OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */

namespace OpticianDB
{
    using System;
    using System.Collections.Generic;
    using System.Data.SQLite;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using OpticianDB.Adaptor;

    public class DBBackEnd : IDisposable
    {
        private DBAdaptor adaptor;
        private SQLiteConnection connection;
        private string connectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="DBBackEnd"/> class.
        /// </summary>
        public DBBackEnd()
        {
            this.connectionString = "DbLinqProvider=Sqlite;Data Source=OpticianDB.db3";
            this.connection = new SQLiteConnection(this.connectionString);
            this.RefreshAdaptor();

            if (!File.Exists("OpticianDB.db3"))
            {
                this.CreateNewDB();
            }
        }

        public IQueryable<string> UserNameList
        {
            get
            {
                return from user in this.adaptor.Users
                       select user.Username;
            }
        }

        public IQueryable<string> ConditionsList
        {
            get
            {
                return from cnds in this.adaptor.Conditions
                       orderby cnds.Condition ascending
                       select cnds.Condition;
            }
        }

        public IQueryable<string> PatientListWithNHSNumber
        {
            get
            {
                var q = from pnts in this.adaptor.Patients
                        orderby pnts.Name, pnts.NhsnUmber ascending
                        select pnts;
                List<string> resultslist = new List<string>();
                foreach (Patients patient in q)
                {
                    string resultstring = patient.NhsnUmber + " - " + patient.Name;
                    resultslist.Add(resultstring);
                }

                return resultslist.AsQueryable();
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void CreateNewDB()
        {
            Stream resourcestream = Assembly.GetExecutingAssembly().GetManifestResourceStream("OpticianDB.blankdb.sql");
            StreamReader textstream = new StreamReader(resourcestream);
            string newdb = textstream.ReadToEnd();

            this.adaptor.ExecuteCommand(newdb, null);

            this.CreateNewUser("admin", "admin", "Default Administrator"); // TODO: messagebox to show addition of a new user?
        }

        public bool LogOn(string userName, string password)
        {
            var q = from user in this.adaptor.Users
                    where user.Username == userName
                    select user;

            if (q.Count() == 0)
            {
                return false;
            }

            var founduser = q.First();

            string storedpwd = founduser.Password;
            string hashmethod = founduser.PasswordHashMethod;
            string hashedpwd = Hashing.GetHash(password, hashmethod);

            return storedpwd == hashedpwd;
        }

        public bool CreateNewUser(string userName, string password, string fullName)
        {
            if (this.UserExists(userName))
            {
                return false;
            }

            var newuser = new Users();

            newuser.Fullname = fullName;
            newuser.Password = Hashing.GetHash(password, "sha1");
            newuser.Username = userName;
            newuser.PasswordHashMethod = "sha1";

            this.adaptor.Users.InsertOnSubmit(newuser);
            this.adaptor.SubmitChanges();

            return true;
        }

        public bool AmendUser(string editedUser, string newUserName, string password, string fullName)
        {
            if (editedUser != newUserName && this.UserExists(newUserName))
            {
                return false;
            }

            var userrec = (from uq in this.adaptor.Users
                           where uq.Username == editedUser
                           select uq).First();

            if (string.IsNullOrEmpty(password))
            {
                string hashingmethod = userrec.PasswordHashMethod;
                string pwhash = Hashing.GetHash(password, hashingmethod);
                if (pwhash != password)
                {
                    userrec.Password = pwhash;
                }
            }

            if (editedUser != newUserName)
            {
                userrec.Username = newUserName;
            }

            if (fullName != userrec.Fullname)
            {
                userrec.Fullname = fullName;
            }

            this.adaptor.SubmitChanges();
            return true;
        }

        public Users GetUserInfo(string userName)
        {
            var user = (from q in this.adaptor.Users
                        where q.Username == userName
                        select q).First();
            return user;
        }

        public int PatientIDByNHSNumber(string nhsNumber)
        {
            var result = (from pnts in this.adaptor.Patients
                          where pnts.NhsnUmber == nhsNumber
                          select pnts.PatientID).First();
            var resultint = result;
            return resultint;
        }

        public bool UserExists(string userName)
        {
            if (this.adaptor.Users.Count() != 0)
            {
                var existingusers = from user in this.adaptor.Users
                                    where user.Username == userName
                                    select user;
                if (existingusers.Count() != 0)
                {
                    return true;
                }
            }

            return false;
        }

        //rtns -1 if record exists or returns recid
        public int AddPatient(string name, string address, string telNum, DateTime dateOfBirth, string nhsNumber, string email)
        {
            if (NHSNumberExists(nhsNumber))
            {
                return -1;
            }

            Patients pRec = new Patients();
            pRec.Name = name;
            pRec.Address = address;
            pRec.TelNum = telNum;
            pRec.DateOfBirth = dateOfBirth;
            pRec.NhsnUmber = nhsNumber;
            pRec.Email = email;

            this.adaptor.Patients.InsertOnSubmit(pRec);
            this.adaptor.SubmitChanges();

            return pRec.PatientID;
        }

        //assumes exists
        public Patients PatientRecord(int id)
        {
            var pr = (from q in this.adaptor.Patients
                      where q.PatientID == id
                      select q).First();
            return pr;
        }

        public int AddCondition(string conditionName)
        {
            Conditions cnd = new Conditions();

            if (this.ConditionExists(conditionName))
            {
                return -1;
            }

            cnd.Condition = conditionName;
            this.adaptor.Conditions.InsertOnSubmit(cnd);
            this.adaptor.SubmitChanges();

            return cnd.ConditionID;
            //TODO: Description?
        }

        public void RemoveConditionByName(string conditionName, int patientID) //FIXME
        {
            this.RefreshAdaptor();
            //Patients patient = this.PatientRecord(patientID);
            //var condition = (from q in patient.PatientConditions
            //                 where q.Conditions.Condition == conditionName
            //                 select q).First();

            var condition = (from q in this.adaptor.PatientConditions
                             where q.Conditions.Condition == conditionName
                             where q.PatientID == patientID
                             select q).First();
            this.adaptor.PatientConditions.DeleteOnSubmit(condition);
            //patient.PatientConditions.Remove(condition);
            this.adaptor.SubmitChanges();
        }

        public bool ConditionExists(string conditionName)
        {
            var contable = (from q in this.adaptor.Conditions
                            where q.Condition == conditionName
                            select q).Count();
            if (contable != 0)
            {
                return true;
            }

            return false;
        }

        public bool CanPatientBePosted(Patients value)
        {
            return (!string.IsNullOrEmpty(value.Address));
        }

        public bool CanPatientBePhoned(Patients value)
        {
            return (!string.IsNullOrEmpty(value.TelNum));
        }

        public bool CanPatientBeEmailed(Patients value)
        {
            return (!string.IsNullOrEmpty(value.Email));
        }

        public Enums.RecallMethods PatientRecallMethod(int patientId)
        {
            return (Enums.RecallMethods)PatientRecord(patientId).PreferredRecallMethod;
        }
        public Enums.RecallMethods PatientRecallMethod(Patients patient)
        {
            return (Enums.RecallMethods)patient.PreferredRecallMethod;
        }

        public string GetConditionName(int conditionID)
        {
            var con = (from q in this.adaptor.Conditions
                       where q.ConditionID == conditionID
                       select q.Condition).First();
            return con;
        }

        public int ConditionID(string conditionName)
        {
            var con = (from q in this.adaptor.Conditions
                       where q.Condition == conditionName
                       select q.ConditionID).First();
            return con;
        }

        public IQueryable<PatientConditions> PatientConditionList(int patientID)
        {
            var con = from q in this.adaptor.PatientConditions
                      where q.PatientID == patientID
                      select q;
            return con;
        }

        public void AttachCondition(int patientID, int conditionID) //FIXME
        {
            PatientConditions pCond = new PatientConditions();
            pCond.PatientID = patientID;
            pCond.ConditionID = conditionID;
            this.adaptor.PatientConditions.InsertOnSubmit(pCond);
            this.adaptor.SubmitChanges();
        }

        public bool AmmendPatient(int patientID, string name, string address, string telNum, DateTime dateOfBirth, string nhsNumber, string email, Enums.RecallMethods preferredrecallmethod)
        {
            var pRecord = this.PatientRecord(patientID);
            pRecord.Name = name;
            pRecord.Address = address;
            pRecord.TelNum = telNum;
            pRecord.DateOfBirth = dateOfBirth;
            if (pRecord.NhsnUmber != nhsNumber)
            {
                if (NHSNumberExists(nhsNumber))
                    return false;
            }
            pRecord.NhsnUmber = nhsNumber;
            pRecord.Email = email;
            pRecord.PreferredRecallMethod = (int)preferredrecallmethod;

            this.adaptor.SubmitChanges();
            return true;
        }

        public bool NHSNumberExists(string nhsNumber)
        {
            var q = (from qr in this.adaptor.Patients
                     where qr.NhsnUmber == nhsNumber
                     select qr).Count();
            if (q == 1)
            {
                return true;
            }
            return false;
        }

        public void RefreshAdaptor()
        {
            if (this.adaptor != null)
            {
                this.adaptor.Dispose();
            }

            this.adaptor = new DBAdaptor(this.connection);
#if DEBUG //fixme
            this.adaptor.Log = Console.Out;
#endif
        }

        public bool OutstandingRecall(int patientId)
        {
            var rclq = from q in this.adaptor.PatientRecalls
                       where q.PatientID == patientId
                       select q;
            if (rclq.Count() == 0)
            {
                return false;
            }
            return true;
        }

        public PatientRecalls GetRecall(int patientId) //TODO: STORE RECALL METHOD AS ENUM
        {
            return (from q in this.adaptor.PatientRecalls
                    where q.PatientID == patientId
                    select q).First();

        }

        public void DeletePatient(int patientId)
        {
            Patients pRec = PatientRecord(patientId);
            adaptor.PatientRecalls.DeleteAllOnSubmit(pRec.PatientRecalls);
            adaptor.PatientConditions.DeleteAllOnSubmit(pRec.PatientConditions);
            adaptor.PatientAppointments.DeleteAllOnSubmit(pRec.PatientAppointments);
            adaptor.PatientTestResults.DeleteAllOnSubmit(pRec.PatientTestResults);
            adaptor.Patients.DeleteOnSubmit(pRec);
            adaptor.SubmitChanges();
        }

        public void DeleteRecall(int patientId)
        {
            adaptor.PatientRecalls.DeleteOnSubmit(GetRecall(patientId));
            adaptor.SubmitChanges();
        }

        public void SaveRecall(int patientId, DateTime dateAndPrefTime, string reason, Enums.RecallMethods method)
        {
            if (OutstandingRecall(patientId))
            {
                DeleteRecall(patientId);
            }
            PatientRecalls pr1 = new PatientRecalls();
            pr1.PatientID = patientId; //FIXME use add
            pr1.DateAndPrefTime = dateAndPrefTime;
            pr1.Reason = reason;
            pr1.Method = (int)method;
            adaptor.PatientRecalls.InsertOnSubmit(pr1);
            adaptor.SubmitChanges();
        }

        public void AmmendRecall(int patientId, DateTime dateAndPrefTime, string reason, Enums.RecallMethods method)
        {
            PatientRecalls pr1 = GetRecall(patientId);
            pr1.DateAndPrefTime = dateAndPrefTime;
            pr1.Reason = reason;
            pr1.Method = (int)method;
            adaptor.SubmitChanges();
        }

        public IEnumerable<PatientRecalls> TodaysRecalls
        {
            get
            {
                DateTime today = DateTime.Today;
                DateTime tomorrow = DateTime.Today.AddDays(1);
                var Recalls = from q in this.adaptor.PatientRecalls
                              where q.DateAndPrefTime.Value < tomorrow
                              select q;
                return Recalls;
            }
        }

        public IEnumerable<PatientRecalls> RecallList
        {
            get
            {

                var Recalls = from q in this.adaptor.PatientRecalls
                              select q;
                return Recalls;
            }
        }

        public IEnumerable<PatientRecalls> GetRecalls(DateTime? enddate, DateTime? startdate)
        {
            if (enddate.HasValue == false && startdate.HasValue == true)
            {
                var Recalls = from q in this.adaptor.PatientRecalls
                              where q.DateAndPrefTime > startdate
                              select q;
                return Recalls;
            }
            else if (enddate.HasValue == true && startdate.HasValue == false)
            {
                enddate = enddate.Value.AddDays(1);
                var Recalls = from q in this.adaptor.PatientRecalls
                              where q.DateAndPrefTime < enddate
                              select q;
                return Recalls;
            }
            else if (enddate.HasValue == true && startdate.HasValue == true)
            {
                enddate = enddate.Value.AddDays(1);
                var Recalls = from q in this.adaptor.PatientRecalls
                              where q.DateAndPrefTime > startdate
                              where q.DateAndPrefTime < enddate
                              select q;
                return Recalls;
            }
            return RecallList;

        }


        /// <summary>
        /// Releases unmanaged and - optionally - managed resources
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // free managed resources
                this.adaptor.Dispose();
                this.connection.Dispose();
            }
            // free native resources if there are any.
        }
    }
}
