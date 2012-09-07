using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;

namespace AWSTool
{
    class Program
    {
        public static void Main(string[] args)
        {
            //args[0] = volumeID, args[1] = days to hold, args[2] = Name of Snapshot
            //string[] testargs = { "vol-e331af9a", "7", "TestingSnapshotProgram" };
            if (args.Length == 3)
            {
                try
                {
                    Console.Write(GetServiceOutput(args));
                }

                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }                
            }
            else
            {
                Console.WriteLine("AWSTool Usage is AWSTool.exe <volID> <DaysToHold> <SnapshotName>");
                Console.WriteLine("Example: Awstool.exe vol-e331af9a 14 \"Exchange Store Backup\"");
                Console.WriteLine("This would backup volume vol-e331af9a, name it Exchange Store Backup, and delete all snapshots created with this program that are older than 14 days");
            }
            
        }

        public static string GetServiceOutput(string[] args)
        {            
            string VolID = args[0];            
            int DaysToHold = 0;
            Int32.TryParse(args[1], out DaysToHold);
            string SnapshotName = args[2];
            DateTime cutOffDate = DateTime.Now.AddDays(-DaysToHold);
            
            StringBuilder sb = new StringBuilder(1024);
            if (VolID.Trim().Length > 0 || DaysToHold > 0 || SnapshotName.Trim().Length > 0)
            {
                using (StringWriter sr = new StringWriter(sb))
                {

                    try
                    {
                        CreateSnapshot(VolID, SnapshotName);
                        Console.WriteLine(VolID + " snapshot created");
                        CheckForSnapshotDeletion(VolID, cutOffDate);
                    }
                    catch (AmazonEC2Exception ex)
                    {
                        if (ex.ErrorCode != null && ex.ErrorCode.Equals("AuthFailure"))
                        {
                            sr.WriteLine("Authentication Error, Verify access keys.");
                        }
                        else
                        {
                            sr.WriteLine("Caught Exception: " + ex.Message);
                            sr.WriteLine("Response Status Code: " + ex.StatusCode);
                            sr.WriteLine("Error Code: " + ex.ErrorCode);
                            sr.WriteLine("Error Type: " + ex.ErrorType);
                            sr.WriteLine("Request ID: " + ex.RequestId);
                            sr.WriteLine("XML: " + ex.XML);
                        }
                    }
                    sr.WriteLine();
                }
            }
            else
            {
                Console.WriteLine("AWSTool Usage is AWSTool.exe <volID> <DaysToHold> <SnapshotName>");
                Console.WriteLine("Example: Awstool.exe vol-e331af9a 14 \"Exchange Store Backup\"");
                Console.WriteLine("This would backup volume vol-e331af9a, name it Exchange Store Backup, and delete all snapshots created with this program that are older than 14 days");
            }
            return sb.ToString();
        }


        static IEnumerable<Snapshot> GetSnapshotsWithBackupDate()
        {
            AmazonEC2 ec2 = GetEC2Client();
            Filter filter = new Filter().WithName("tag:BackupDate").WithValue("*");
            var request = new DescribeSnapshotsRequest().WithFilter(filter);
            var response = ec2.DescribeSnapshots(request);
            return response.DescribeSnapshotsResult.Snapshot;
        }
        static AmazonEC2 GetEC2Client()
        {
            return AWSClientFactory.CreateAmazonEC2Client(new AmazonEC2Config().WithServiceURL("https://us-west-1.ec2.amazonaws.com"));
        }

        static Snapshot CreateSnapshot(string VolumeID, string name)
        {
            AmazonEC2 ec2 = GetEC2Client();
            var request = new CreateSnapshotRequest()
                            .WithVolumeId(VolumeID)
                            .WithDescription(name);
            var response = ec2.CreateSnapshot(request);
            var snapshot = response.CreateSnapshotResult.Snapshot;
            ec2.CreateTags(new CreateTagsRequest()
                                .WithResourceId(snapshot.SnapshotId)
                                .WithTag(new Tag { Key = "Name", Value = name })
                                .WithTag(new Tag { Key = "BackupDate", Value = DateTime.Today.ToShortDateString() }));

            while (CheckSnapshotCompletion(snapshot.SnapshotId) == false)
            {
                System.Threading.Thread.Sleep(5000);
                Console.WriteLine("Checking Status");
            }

            return snapshot;
        }
        static void CheckForSnapshotDeletion(string VolID, DateTime cutOffDate)
        {
            foreach (Snapshot s in GetSnapshotsWithBackupDate())
            {

                if (s.VolumeId == VolID)
                {
                    foreach (Tag t in s.Tag)
                    {

                        DateTime ParsedDate = DateTime.MinValue;
                        if (t.Key == "BackupDate" && DateTime.TryParse(t.Value, out ParsedDate))
                        {
                            if (ParsedDate < cutOffDate)
                            {
                                DeleteSnapshot(s.SnapshotId);
                            }
                        }

                    }
                }

            }

        }
        static bool CheckSnapshotCompletion(string snapshotID)
        {
            bool status = false;
            AmazonEC2 ec2 = GetEC2Client();
            Filter filter = new Filter();
            var request = new DescribeSnapshotsRequest().WithSnapshotId(snapshotID);
            var response = ec2.DescribeSnapshots(request);
            foreach (Snapshot s in response.DescribeSnapshotsResult.Snapshot)
            {
                if (s.SnapshotId == snapshotID)
                {
                    if (s.Status == "completed")
                    {
                        status = true;
                    }

                }

            }
            return status;
        }
        static void DeleteSnapshot(string SnapshotID)
        {
            AmazonEC2 ec2 = GetEC2Client();
            var request = new DeleteSnapshotRequest().WithSnapshotId(SnapshotID);
            var response = ec2.DeleteSnapshot(request);
            Console.WriteLine(SnapshotID + "was Deleted");
        }

    }
}