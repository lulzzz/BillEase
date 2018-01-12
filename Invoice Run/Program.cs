﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Invoice_Run.BillingSite;
using Microsoft.SharePoint.Client;
using System.Diagnostics;
using NDesk.Options;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Net;

namespace Invoice_Run
{
    class Program
    {
        const string evtLogSrc = "Service Billing Invoice Run";
        static void Main(string[] args)
        {
            if (!EventLog.SourceExists(evtLogSrc))
            {
                EventLog.CreateEventSource(evtLogSrc, "Application");
            }

            try
            {
                string groupPrefix = "";
                int cycleMonthOffset = -1;
                Dictionary<string, List<string>> listColumnsToCopy = new Dictionary<string, List<string>>();
                listColumnsToCopy.Add("Organization", new List<string>());
                listColumnsToCopy.Add("Rate", new List<string>());
                var options = new OptionSet(){
                    {"p|prefix_of_group=", v => groupPrefix = v}
                    ,{"o|offset_of_cycle_month=", v => cycleMonthOffset = int.Parse(v)}
                    ,{"g|organization_columns_to_copy=", v => listColumnsToCopy["Organization"].Add(v)}
                    ,{"r|rate_columns_to_copy=", v => listColumnsToCopy["Rate"].Add(v)}
                };
                List<String> extraArgs = options.Parse(args);
                ServicePointManager.ServerCertificateValidationCallback = MyCertHandler;
                var dc = new BillingsDataContext(new Uri(extraArgs[0] + "/_vti_bin/ListData.svc"));
                //dc.IgnoreMissingProperties = true;
                dc.Credentials = System.Net.CredentialCache.DefaultCredentials;
                var cycleDt = DateTime.Now.AddMonths(cycleMonthOffset);
                var consumptions = from consumption in dc.Consumptions
                                   where consumption.Cycle.Value.Month == cycleDt.Month
                                   && consumption.Cycle.Value.Year == cycleDt.Year
                                   select consumption;
                foreach (var consumption in consumptions)
                {
                    consumption.Rate = dc.Rates.Where(r => r.Id == consumption.RateId).First();
                    consumption.Organization = dc.Organizations.Where(o => o.Id == consumption.OrganizationId).First();
                    var existingCiCnt = dc.Charges.Where(c => c.ConsumptionRefId == consumption.Id).Count();
                    if (existingCiCnt > 0) continue;
                    var ci = new ChargesItem();
                    ci.Organization = consumption.Organization.Name;
                    ci.Title = consumption.Title;
                    ci.Cycle = consumption.Cycle;
                    ci.UnitPrice = consumption.Rate.UnitPrice;
                    ci.Denominator = consumption.Rate.Denominator;
                    ci.UOM = consumption.Rate.UOMValue;
                    ci.Quantity = consumption.Quantity;
                    ci.ConsumptionRefId = consumption.Id;
                    if (consumption.Amount.HasValue)
                        ci.Amount = consumption.Amount.Value;
                    else if (consumption.Quantity.HasValue
                        && consumption.Rate.Denominator.HasValue
                        && consumption.Rate.UnitPrice.HasValue
                        && consumption.Rate.Denominator.Value > 0)
                        ci.Amount = consumption.Rate.UnitPrice * Math.Ceiling(consumption.Quantity.Value / consumption.Rate.Denominator.Value);
                    if (ci.Amount.HasValue)
                        dc.AddToCharges(ci);
                    else
                    {
                        EventLog.WriteEntry(evtLogSrc, "Cannot calculate amount for consumption item #" + consumption.Id, EventLogEntryType.Error);
                    }
                    foreach (KeyValuePair<string, List<string>> listColumnToCopy in listColumnsToCopy)
                    {
                        if (listColumnToCopy.Value.Count <= 0)
                        {
                            continue;
                        }
                        foreach (var columnNm in listColumnToCopy.Value)
                        {
                            var x = consumption.GetType().GetProperty(listColumnToCopy.Key).GetValue(consumption, null);
                            var y = x.GetType().GetProperty(columnNm).GetValue(x, null);
                            ci.GetType().GetProperty(columnNm).SetValue(ci, y, null);
                        }

                    }

                }
                dc.SaveChanges();
                var cc = new ClientContext(extraArgs[0]);
                cc.Credentials = System.Net.CredentialCache.DefaultCredentials;
                var chargesLst = cc.Web.Lists.GetByTitle("Charges");
                var query = new CamlQuery();
                query.ViewXml = string.Format(@"
<View><Query>
   <Where>
      <Eq>
         <FieldRef Name='Cycle' />
         <Value Type='DateTime'>{0}</Value>
      </Eq>
   </Where>
</Query></View>", DateTime.Now.AddMonths(cycleMonthOffset).ToString("yyyy-MM-01"));

                var chargesLIC = chargesLst.GetItems(query);
                cc.Load(chargesLIC, items => items.Include(
                    item => item["Organization"]
                    , item => item["HasUniqueRoleAssignments"]
                    ));
                var consumptionLst = cc.Web.Lists.GetByTitle("Consumptions");
                var consumptionLIC = consumptionLst.GetItems(query);
                cc.Load(consumptionLIC, items => items.Include(
                    item => item["HasUniqueRoleAssignments"]
                    ));
                cc.Load(cc.Web.RoleDefinitions);
                var gc = cc.Web.SiteGroups;
                cc.Load(gc);
                cc.ExecuteQuery();
                var restReadRD = cc.Web.RoleDefinitions.GetByName("Restricted Read");
                var readRD = cc.Web.RoleDefinitions.GetByName("Read");
                foreach (var chargeLI in chargesLIC)
                {
                    if (!chargeLI.HasUniqueRoleAssignments)
                    {
                        chargeLI.BreakRoleInheritance(true, false);
                    }
                    foreach (var g in gc)
                    {
                        if (g.LoginName == (groupPrefix + chargeLI["Organization"].ToString()))
                        {
                            var rdb = new RoleDefinitionBindingCollection(cc);
                            rdb.Add(restReadRD);
                            chargeLI.RoleAssignments.Add(g, rdb);
                            break;
                        }
                    }
                }
                foreach (var consumptionLI in consumptionLIC)
                {
                    if (!consumptionLI.HasUniqueRoleAssignments)
                    {
                        consumptionLI.BreakRoleInheritance(true, false);
                        cc.ExecuteQuery();
                    }
                    cc.Load(consumptionLI.RoleAssignments, items => items.Include(
                        ra => ra.RoleDefinitionBindings.Include(
                            rdb => rdb.Name
                            )
                        ));
                    cc.ExecuteQuery();

                    foreach (var ra in consumptionLI.RoleAssignments)
                    {
                        bool addRead = false;
                        foreach (var rdbo in ra.RoleDefinitionBindings)
                        {
                            if (rdbo.Name == "Contribute")
                            {
                                ra.RoleDefinitionBindings.Remove(rdbo);
                                addRead = true;
                            }
                        }
                        if (addRead) ra.RoleDefinitionBindings.Add(readRD);
                        ra.Update();
                    }
                    cc.ExecuteQuery();
                }

            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(evtLogSrc, ex.ToString(), EventLogEntryType.Error);
                throw;
            }
        }

        static bool MyCertHandler(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors error)
        {
            // Ignore errors
            return true;
        }
    }
}
