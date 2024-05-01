﻿using ClosedXML.Excel;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Office2010.Excel;
using DocumentFormat.OpenXml.Office2016.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using HalloDoc.DataLayer.Data;
using HalloDoc.DataLayer.Models;
using HalloDoc.DataLayer.ViewModels;
using HalloDoc.LogicLayer.Patient_Interface;
using HalloDocMvc.Entity.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Path = System.IO.Path;
using System.Xml.Linq;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using System.IO;
using Paragraph = iText.Layout.Element.Paragraph;
using Document = iText.Layout.Document;
using iText.Layout.Properties;
using iText.Kernel.Font;
using TextAlignment = iText.Layout.Properties.TextAlignment;
using iText.Kernel.Font;
using DocumentFormat.OpenXml.Spreadsheet;
using static System.Runtime.InteropServices.JavaScript.JSType;
using iText.IO.Font.Constants;
using Style = iText.Layout.Style;

namespace HalloDoc.LogicLayer.Patient_Repository
{
    public class ProviderInterface : IProviderInterface
    {
        private readonly ApplicationDbContext _context;
        private readonly IAdminInterface _adminInterface;

        public ProviderInterface(ApplicationDbContext context, IAdminInterface adminInterface)
        {
            _context = context;
            _adminInterface = adminInterface;
        }
        public bool ConcludeCaseSubmitAction(ViewUploadsModel model, int id, Physician p)
        {
            bool isConcluded = true;
            Request r = _adminInterface.GetReqFromReqId(id);
            RequestNote rn = _adminInterface.FetchRequestNote(id);
            RequestStatusLog rsl = new RequestStatusLog();
            RequestNote rn2 = new RequestNote();
            r.Status = 8;
            r.ModifiedDate = DateTime.Now;
            r.CompletedByPhysician = new BitArray(1, true);

            if(rn==null)
            {
                rn2.RequestId = id;
                rn2.PhysicianNotes = model.providerNotes;
                rn2.CreatedDate = DateTime.Now;
                rn2.CreatedBy = (int)p.AspNetUserId;
                _context.RequestNotes.Add(rn2);
            }

            if(rn!=null)
            {
                rn.PhysicianNotes = rn.PhysicianNotes + ", " + model.providerNotes;
                rn.ModifiedDate = DateTime.Now;
                rn.CreatedBy = (int)p.AspNetUserId;
                _context.RequestNotes.Update(rn);
            }

            rsl.RequestId = id;
            rsl.Status = 8;
            rsl.PhysicianId = p.PhysicianId;
            rsl.Notes = model.providerNotes;
            rsl.CreatedDate = DateTime.Now;
            _context.RequestStatusLogs.Add(rsl);

            _context.SaveChanges();
            return isConcluded;
        }

        public List<ShiftDetail> GetProviderScheduleData(int id)
        {
            try
            {
                return _context.ShiftDetails.Include(m => m.Shift).Where(m => m.Shift.PhysicianId == id && m.IsDeleted == new System.Collections.BitArray(new[] { false })).ToList();

            }
            catch { return new List<ShiftDetail> { }; }
        }

        public List<SchedulingViewModel> GetProviderInformation(int phyId)
        {
            try
            {
                var physician = _context.PhysicianRegions.Include(m => m.Physician).Where(m => m.PhysicianId == phyId);


                List<SchedulingViewModel> model = new List<SchedulingViewModel>();
                foreach (var item in physician)
                {
                    if (item.Physician.IsDeleted == null || item.Physician.IsDeleted[0] == false)
                    {
                        SchedulingViewModel providerInformationViewModel = new SchedulingViewModel()
                        {
                            physicianId = item.Physician.PhysicianId,
                            ProviderName = item.Physician.FirstName + " " + item.Physician.LastName,
                            ProviderEmail = item.Physician.Email,
                            Role = item.Physician.RoleId.ToString(),
                            Status = item.Physician.Status.ToString()
                        };
                        model.Add(providerInformationViewModel);
                    }
                }
                return model.ToList();
            }
            catch
            {
                return new List<SchedulingViewModel>();
            }
        }

        public List<Region> GetProviderRegionFromId(int id)
        {
            List<Region>regions = new List<Region>();
            List<PhysicianRegion> pr = _context.PhysicianRegions.Where(p => p.PhysicianId == id).ToList();

            foreach(var item in pr)
            {
                Region r = _context.Regions.FirstOrDefault(re => re.RegionId == item.RegionId);
                regions.Add(r);
            }

            return regions;
        }

        public bool CreateNewShift(SchedulingViewModel model, List<int> RepeatedDays, int id)
        {
            // one entry in shift and multiple entries in shiftdetail


            Shift shift = new Shift();
            shift.PhysicianId = (int)model.physicianId;
            shift.StartDate = DateOnly.FromDateTime((DateTime)model.startDate);

            // check if shift is repeated or not
            if (model.repeat != 0)
            {
                shift.IsRepeat = new BitArray(1, true);
            }
            else
            {
                shift.IsRepeat = new BitArray(1, false);
            }

            // if shift is repeated and checkboxes are checked, set 1 to those weekdays in shift.WeekDays else set 0
            if (shift.IsRepeat != new BitArray(1, false) && RepeatedDays != null)
            {

                for (int i = 0; i < 7; i++)
                {
                    if (RepeatedDays!.Any(u => u == i))
                    {
                        shift.WeekDays = shift.WeekDays + "1";
                    }
                    else
                    {
                        shift.WeekDays = shift.WeekDays + "0";
                    }
                }
            }
            else
            {
                shift.WeekDays = "0000000";
            }

            shift.RepeatUpto = model.repeat;
            shift.CreatedBy = id;
            shift.CreatedDate = DateTime.Now;
            _context.Shifts.Add(shift);
            _context.SaveChanges();

            // common entry of shift whose startDate = model.startDate
            ShiftDetail sd = new ShiftDetail();
            sd.Shift = shift;
            sd.ShiftDate = (DateTime)model.startDate;
            sd.RegionId = model.regionId;
            sd.StartTime = (TimeOnly)model.startTime;
            sd.EndTime = (TimeOnly)model.endTime;
            sd.Status = 0;
            sd.IsDeleted = new BitArray(1, false);
            _context.ShiftDetails.Add(sd);

            // if shift is repeated and atleast one checkbox is checked
            if (shift.IsRepeat != new BitArray(1, false) && RepeatedDays != null)
            {
                int current = 0; //variable to count and stop when the total numbered of entered data is greater than 'total'
                int total = RepeatedDays.Count() * (int)model.repeat; //total number of entries in ShiftDetail other than current entry (when we submit)

                for (int i = 0; i <= model.repeat; i++)
                {
                    DateTime shiftDate = (DateTime)model.startDate;
                    DateTime tempdate = new DateTime();

                    // when we want to store the repeated shift related data in current week
                    if (i == 0)
                    {
                        // if day of shiftDate is wednesday, then 0-4 = -4 i.e. day of tempdate would be sunday
                        tempdate = shiftDate.AddDays((7 * i) - (int)shiftDate.DayOfWeek);

                        // make entry for each checked day
                        foreach (var day in RepeatedDays)
                        {
                            // if checked day is greater than day of shiftDate, which is Sunday
                            if (day > (int)shiftDate.DayOfWeek)
                            {
                                int count = day;
                                ShiftDetail shiftDetail1 = new ShiftDetail();
                                shiftDetail1.Shift = shift;
                                shiftDetail1.ShiftDate = tempdate.AddDays(count);
                                shiftDetail1.RegionId = model.regionId;
                                shiftDetail1.StartTime = (TimeOnly)model.startTime!;
                                shiftDetail1.EndTime = (TimeOnly)model.endTime!;
                                shiftDetail1.Status = 0;
                                shiftDetail1.IsDeleted = new BitArray(1, false);
                                _context.ShiftDetails.Add(shiftDetail1);
                                current++;
                            }
                        }
                    }

                    // to store data for the shift which is going to repeat in next week
                    else
                    {
                        // start from Sunday of next week
                        tempdate = shiftDate.AddDays((7 * i) - (int)shiftDate.DayOfWeek);
                        for (int j = 0; j < 7; j++)
                        {
                            // break when number of entered entries in ShiftDetail increases than total variable
                            if (total <= current)
                            {
                                break;
                            }

                            // check if j ==  any of the checked day and if that is true, do entry in table and set current = current + 1;
                            if (RepeatedDays.Any(r => r == j))
                            {
                                ShiftDetail shiftDetail2 = new ShiftDetail();
                                shiftDetail2.Shift = shift;
                                shiftDetail2.ShiftDate = tempdate.AddDays(j);
                                shiftDetail2.RegionId = model.regionId;
                                shiftDetail2.StartTime = (TimeOnly)model.startTime!;
                                shiftDetail2.EndTime = (TimeOnly)model.endTime!;
                                shiftDetail2.Status = 0;
                                shiftDetail2.IsDeleted = new BitArray(1, false);

                                _context.ShiftDetails.Add(shiftDetail2);
                                current = current + 1;
                            }
                        }
                    }
                }
            }

            _context.SaveChanges();
            return true;
        }

        public EditProviderAccountViewModel GetProviderProfile(int id, AdminNavbarModel an)
        {
            var physician = _context.Physicians.FirstOrDefault(r => r.PhysicianId == id);
            List<PhysicianRegion> PRegions = _context.PhysicianRegions.Where(r => r.PhysicianId == physician.PhysicianId).ToList();
            List<DataLayer.Models.Region> reg = _context.Regions.ToList();
            var selectedRegions = from r in reg
                                  join pr in PRegions
                                  on r.RegionId equals pr.RegionId
                                  select r;
            var data = selectedRegions.ToList();
            AspNetUser user = _context.AspNetUsers.FirstOrDefault(r => r.Id == physician.AspNetUserId);

            EditProviderAccountViewModel viewmodel = new EditProviderAccountViewModel
            {
                UserName = user.UserName,
                FirstName = physician.FirstName,
                LastName = physician.LastName,
                Password = user.PasswordHash,
                Email = physician.Email,
                ConfirmEmail = "",
                Phone = physician.Mobile,
                regions = _context.Regions.ToList(),
                selectedregions = data,
                Address1 = physician.Address1,
                Address2 = physician.Address2,
                City = physician.City,
                State = physician.City,
                Zip = physician.Zip,
                MedicalLicense = physician.MedicalLicense,
                NPI = physician.Npinumber,
                SyncEmail = physician.SyncEmailAddress,
                MailingPhoneNo = physician.AltPhone,
                BusinessName = physician.BusinessName,
                BusinessWebsite = physician.BusinessWebsite,
                SignatureName = physician.Signature,
                PhysicianId = id,
                Contract = physician.IsAgreementDoc != null ? physician.IsAgreementDoc[0] : null,
                BackgroundCheck = physician.IsBackgroundDoc != null ? physician.IsBackgroundDoc[0] : null,
                Compilance = physician.IsTrainingDoc != null ? physician.IsTrainingDoc[0] : null,
                NonDisclosure = physician.IsNonDisclosureDoc != null ? physician.IsNonDisclosureDoc[0] : null,
                LicensedDoc = physician.IsLicenseDoc != null ? physician.IsLicenseDoc[0] : null,
                adminNavbarModel = an,
                Photo = null,
                roles = _context.Roles.Where(r => r.AccountType == (short)2).ToList(),
                regionId = physician.RegionId
            };
            return viewmodel;
        }

        public List<Admin> GetAllAdmins()
        {
            return _context.Admins.ToList();
        }

        public bool isEncounterFinalized(int id)
        {
            EncounterForm ef = _context.EncounterForms.FirstOrDefault(e => e.RequestId == id);
            return ef.IsFinalized[0];
        }

        public InvoicingViewModel GetBiWeeklyTimesheet(DateTime startDate, DateTime endDate, AdminNavbarModel an, int userId)
        {
            Physician p = _context.Physicians.FirstOrDefault(Physician => Physician.AspNetUserId == userId);
            List<KeyValuePair<string, int>> onCallHour = new List<KeyValuePair<string, int>>();
            int j = 0;

            for (int i = startDate.Day; i <= endDate.Day; i++)
            {
                DateTime temp = startDate.AddDays(j);
                List<ShiftDetail> sd = _context.ShiftDetails.Where(s => s.Shift.PhysicianId == p.PhysicianId && s.ShiftDate == temp).ToList();
                var totalHours = 0;
                foreach (var item in sd)
                {
                    TimeSpan startTime = TimeSpan.Parse(item.StartTime.ToString());
                    TimeSpan endTime = TimeSpan.Parse(item.EndTime.ToString());
                    double ans = endTime.Subtract(startTime).TotalHours;
                    totalHours += Convert.ToInt32(ans);
                }
                // Specify the date format here
                string formattedDate = temp.ToString("MM/dd/yyyy");
                onCallHour.Add(new KeyValuePair<string, int>(formattedDate, totalHours));
                j++;
            }

            InvoicingViewModel ivm = new InvoicingViewModel
            {
                adminNavbarModel = an,
                dateAndOnCallHour = onCallHour,
            };

            return ivm;

        }
    }
}
