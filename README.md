BillEase
==========================

A service billing SharePoint web application designed for the purpose of intra-organization cost allocation supporting recurring charges.

## Background
In a large organization, a support division often provides ongoing services to other divisions. For example, IT department maintains a web site for marketing department. To better keep track of cost allocation, it is often necessary to price the services and charge the client divisions based on certain consumption metrics, similar to what utility companies do to you. It is more so if the support division adopts cost-recovery model. Unlike the case of utility companies, however, implicit trust exists among divisions within an organization. Therefore the requirements of invoicing, payment and collection can be greatly simplified or even eliminated. What's left over are, in essence:

  * Defining services and rates
  * Periodical (monthly, for example) metering
  * Calculating charges based on rates and meter readings at the end of each billing cycle
  * Providing a customer portal for clients to retrieve their consumption and charges. 

*BillEase* is a service billing web application to support above trimmed down functions. It is not, however, intended to replace a corporate accounting system that tracks the flow of funds from one cost center to another.

## Features

  * Support both recurring and one-time fixed rate charges and credits
  * Allow overriding calculated line item amount
  * Automatically prevent charges and consumption records in closed billing cycles from modifications
  * Features inherited from SharePoint
     * Bulk importing and updating
     * Versioning
     * Single-Sign-On with enterprise-wide identity management system, assuming the system is Active Directory
     * Permission at a granularity down to item level
     * Auditing information such as creator, last modifier, creation time and last modified time
     * Recycle Bin
     * Export to Excel

## Overview
*BillEase* consists of four SharePoint custom lists packaged into a SharePoint site template solution file *Service Billing.wsp* and a console application called *Invoice Run.exe*. The custom lists are:
  * *Organizations* - contain client information
  * *Rates* - define services and corresponding rates
  * *Consumptions* - used to upload meter readings
  * *Charges* - line items with amount computed from rates and consumption. 

*Invoice Run.exe* is intended to be put into a scheduled task to run at the end of each billing cycle to create charge line items.

*BillEase* requires following manual processes performed by the service provider:

1. One-time activities - performed first time as initial setup and update only when information changes thereafter.
   1. Populate *Organizations* and *Rates*. 
   2. For each *Organization*, create a SharePoint group named after it. A prefix is allowed. For example, if the organization is called *Marketing*, the corresponding SharePoint group should be called *Billing Group - Marketing*, assuming prefix is *Billing Group -*. The prefix has to be consistent. Change the group name whenever organization name changes.
   3. Add client users who are allowed to see charges to an organization to the corresponding group.
2. Recurring activities:
   1. Near the end of each billing cycle, stage meter readings into an Excel spreadsheet with column order matching the *Consumptions* list view. Put any one-off charges or credits into the spreadsheet as well. 
   2. Select the range of data in Excel and press Ctrl-C to copy. Open the *Datasheet View* of *Consumptions* list in Internet Explorer. click the second column (by default *Title*) of the last empty row in the *Datasheet View* marked by asterisk, and press Ctrl-V to paste the data range to the list. This completes the bulk loading process. Data uploaded can be modified as long as the billing cycle is not closed.

Once above activities are performed, rest processes are handled automatically by *Invoice Run.exe*. Clients can see their charge line items in *Charges* list. They can export the list to Excel for further analysis.

## Components
### SharePoint Custom Lists
*BillEase* depends on list and column names described below to function. Extending the lists are allowed as long as these names are not altered.
#### Organizations
*Organizations* contain client information. Only *Name* column is mandatory. Changing the name of an organization is allowed. However, the value of *Organization* column in *Charges* list is copied from, not referencing to, the *Name* column of *Organizations* list, so the organization name change will not propagate to *Charges* list.
Deleting an organization is disallowed unless all consumption records associated with the organization are deleted.
#### Rates
*Rates* define services and corresponding rates. It has following columns:
  * Title - name of the service
  * Unit Price
  * UOM - unit of measure
  * Denominator - a number combined with UOM to form the denominator of the rate. Denominator is used for round-up calculation. For example, let's say the service being provided is data storage, and the price is $10 per 5GB per month. In this case Unit Price is $10, UOM is GB, and denominator is 5. When priced this way, consumption is rounded-up at the incremental of 5GB. For instance, 6GB costs $20, as opposed to $12 if the rate is defined quasi-equivalently as $2 per GB per month.

Changing *Unit Price* or *Denominator* only affects future charge calculations. Deleting a rate entry is disallowed unless all consumption records associated with the rate are deleted.

#### Consumptions
*Consumptions* list is used to upload meter readings. It has following columns:
  * Title - consumption title. This becomes the charge line item description.
  * Organization - a reference to the organization
  * Rate - a reference to the rate
  * Quantity - consumption data. UOM should match that of rate referenced
  * Cycle - the  start of billing cycle. The first day of current month is populated by default.
  * Amount - used to override calculated amount. This column is useful to post one-off type of charges or credits. When this column is populated, *Quantity* doesn't need to be populated. Even if *Quantity* is populated, the quantity will not be used.

Consumption items are modifiable prior to the closing date of billing cycle and read-only thereafter, except for site collection administrator who have full access. Deleting a consumption item of a closed billing cycle is disallowed unless the corresponding charge item is deleted.
 
#### Charges
Items in *Charges* list are created by *Invoice Run.exe*. There is a one-to-one mapping between *Consumptions* and *Charges*. *Charges* contain following columns:
  * Title - copied from  *Consumptions*
  * Organization - copied from *Consumptions*
  * Cycle - copied from *Consumptions*
  * Unit Price - copied from *Rates* referenced by the corresponding *Consumptions* item
  * Denominator - copied from *Rates* referenced by the corresponding *Consumptions* item
  * UOM - copied from *Rates* referenced by the corresponding *Consumptions* item
  * Quantity - copied from *Consumptions*
  * Amount - either copied from *Consumptions* or, in absence of value, calculated using formula *Unit Price\*Ceiling(Quantity/Denominator)*
  * Consumption Ref  - a hidden field referencing to the corresponding *Consumptions* item

Notice that except for the hidden *Consumption Ref* column, all columns are copied from, rather than referencing to other lists. This *de-normalization* process prevents historical billing records from altering by factors such as organization re-naming or price adjustment, resulting in improved accountability.

By the same record-preserving principle, charge line items should be made read-only, except for site-collection administrators who have full access regardless of permissions. When a charge item is created, the permission of the item is broken from inheritance. Users who have read permissions defined in the *Charges* list at the time of broken can still read the item. In addition, users who belong to the *"&lt;prefix&gt;&lt;organization&gt;"* group are also granted read-only access. This makes the list security-trimmed and suitable to be exposed as a portal page to clients who can only see the charges applied to their organization.

### Console Application
The gem of *BillEase* is the console application *Invoice Run.exe*. It provides automation and turns the four SharePoint lists into a workable solution. Without it the SharePoint lists are merely data repository. *Invoice Run.exe* is intended to be launched by a scheduled task at the close of each billing cycle (by default first day of each month). For testing purpose it can also be launched manually and repetitively. When invoked, *Invoice Run.exe* performs following tasks:

1. For each consumption item in previous billing cycle, create a charge item if not already exists. The values of charge item are copied or calculated using data directly or indirectly obtained from consumption item as described in [Charges](#charges) list above.
2. Break the permission inheritance of each consumption item in previous billing cycle if not already done so. Then convert all *Contribute* permissions to *Read*.
3. Break the permission inheritance of each charge item created in previous billing cycle if not already done so. 
4. For each charge item in previous billing cycle, grant group *"&lt;prefix&gt;&lt;organization&gt;"* read-only access if not already done so.

*Invoice Run.exe* expects following call syntax:
```
"Invoice Run.exe" [options] <URL>
where <URL> points to the site holding the four lists and [options] are
-p|--prefix_of_group=<string>
     Prefix of the client organization groups. The prefix is useful to 
     prevent group name conflicts with other groups defined in same site collection
-o|--offset_of_cycle_month=<number>
     Offset of billing cycle month adjustment. Default to -1. For example, 
     if billing cycle starts on the first day of each month and Invoice Run 
     is launched at 12:01AM on the first day of each month, the default offset 
     of -1 is needed for calculation be performed on last month's data.
-g|--organization_columns_to_copy=<string>
     Name of custom column in organizations list to copy over to charges list. 
	 Multiple columns can be defined by adding this option multiple times. The column
	 must have been defined in both organizations and charges lists indentically in 
	 terms of type and name.
-r|--rate_columns_to_copy=<string>
     Name of custom column in rates list to copy over to charges list. 
	 Multiple columns can be defined by adding this option multiple times. The column
	 must have been defined in both rates and charges lists indentically in 
	 terms of type and name.
-c|--consumption_columns_to_copy=<string>
     Name of custom column in consumptions list to copy over to charges list. 
	 Multiple columns can be defined by adding this option multiple times. The column
	 must have been defined in both consumptions and charges lists indentically in 
	 terms of type and name.


Examples:
"Invoice Run.exe" -p "Billing Group - " https://mycorp.com/service/billing
  Set the prefix of all SharePoint groups used to grant clients accessing 
  the Charges table to "Billing Group - ". Client users from organization 
  Marketing, for example, should be placed in a SharePoint group 
  called "Billing Group - Marketing" 
"Invoice Run.exe" -o 0 https://mycorp.com/service/billing
  Set the offset of billing cycle month adjustment to 0. This is needed if billing 
  cycle starts on the first day of each month and Invoice Run is launched at
  11:50PM on the last day of each month, for example.
"Invoice Run.exe" -c Comments -c Service_x0020_Date https://mycorp.com/service/billing
  When creating charges items, copy Comments and Service_x0020_Date columns in consumptons 
  list over to charges list.
```
## System Requirements and Access Privileges
* Site collection administrator level of access to any edition of SharePoint 2010 or 2013.
* Local administrator access to a server with .Net Framework 4 installed to run scheduled tasks. The server doesn't need to be the host of the SharePoint site. Windows Server 2008 R2 has been tested working.
* Optionally Git client to download package
* Optionally Visual Studio 2017 if you want to compile or change source code of  *Invoice Run.exe*

## Installation
1. Use *Download Zip* button to download the latest version, or use Git client to clone the git repo. 
2.  Upload file *Service Billing.wsp* to SharePoint site collection solution gallery and activate it. 
3. Create a site using *Service Billing* site template contained in *Service Billing.wsp*. You may need to active certain site collection features first.
4. Define permissions of each list appropriately. Users from service provider organization, depending on job roles, should have read-only permission to *Charges* and read-write permission to *Organizations*, *Rates* and *Consumptions*. Don't grant any permission to clients at list level.
5. Follow manual processes in [Overview](#overview) section to populate lists and SharePoint groups. Create some fake data in *Consumptions* list in order to verify the function.
6. Copy all files under */Invoice Run/bin/Debug* to a server where *Invoice Run* scheduled task will be created. The server must have .Net Framework 4 installed. 
7. Manually run *Invoice Run.exe* on the server with URL of the site created in Step 3. above and optional arguments documented in the [Console Application](#console-application) section above. The Windows log in account should be a site collection administrator as well as a local server administrator. If you run from a desktop version of Windows such as Vista with UAC, you have to run *Invoice Run.exe* from a DOS prompt started with "Run as administrator". If the run is successful, you should see new items created in *Charges* list with unique permissions. If the run fails, errors are output to both console and Windows event log.
8. Create a scheduled task to run *Invoice Run.exe* periodically. The account used to run the scheduled task should have adequate privilege to modify list items and permissions. Make the account a site collection administrator is recommended.

## License

The MIT License (MIT)

Copyright (c) 2014-present @abbr

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
