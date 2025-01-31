﻿using Microsoft.AspNetCore.Mvc;
using AutomationForm.Models;
using AutomationForm.Services;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using Azure;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Net;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;

namespace AutomationForm.Controllers
{
    public class LandscapeController : Controller
    {

        private readonly ITableStorageService<LandscapeEntity> _landscapeService;
        private readonly ITableStorageService<AppFile> _appFileService;
        private LandscapeViewModel landscapeView;
        private readonly IConfiguration _configuration;
        private RestHelper restHelper;

        public LandscapeController(ITableStorageService<LandscapeEntity> landscapeService, ITableStorageService<AppFile> appFileService, IConfiguration configuration)
        {
            _landscapeService = landscapeService;
            _appFileService = appFileService;
            _configuration = configuration;
            restHelper = new RestHelper(configuration);
            landscapeView = SetViewData();
        }
        private LandscapeViewModel SetViewData()
        {
            landscapeView = new LandscapeViewModel();
            landscapeView.Landscape = new LandscapeModel();
            try
            {
                ParameterGroupingModel basicParameterArray = Helper.ReadJson("ParameterDetails/BasicLandscapeDetails.json");
                ParameterGroupingModel advancedParameterArray = Helper.ReadJson("ParameterDetails/AdvancedLandscapeDetails.json");
                ParameterGroupingModel expertParameterArray = Helper.ReadJson("ParameterDetails/ExpertLandscapeDetails.json");

                landscapeView.ParameterGroupings = new ParameterGroupingModel[] { basicParameterArray, advancedParameterArray, expertParameterArray };
            }
            catch
            {
                landscapeView.ParameterGroupings = new ParameterGroupingModel[0];
            }

            return landscapeView;
        }

        [ActionName("Index")]
        public async Task<IActionResult> Index()
        {
            LandscapeIndexModel landscapeIndex = new LandscapeIndexModel();

            try
            {
                List<LandscapeEntity> landscapeEntities = await _landscapeService.GetAllAsync();
                List<LandscapeModel> landscapes = landscapeEntities.FindAll(l => l.Landscape != null).ConvertAll(l => JsonConvert.DeserializeObject<LandscapeModel>(l.Landscape));
                landscapeIndex.Landscapes = landscapes;

                List<AppFile> appfiles = await _appFileService.GetAllAsync();
                landscapeIndex.AppFiles = appfiles.FindAll(file => file.Id.EndsWith("INFRASTRUCTURE.tfvars"));
            }
            catch (Exception e)
            {
                TempData["error"] = "Error retrieving existing workload zones: " + e.Message;
            }

            return View(landscapeIndex);
        }

        [HttpGet]
        public async Task<ActionResult> GetWorkloadZones()
        {
            List<SelectListItem> options = new List<SelectListItem>
            {
                new SelectListItem { Text = "", Value = "" }
            };
            try
            {
                List<LandscapeEntity> landscapeEntities = await _landscapeService.GetAllAsync();

                foreach (LandscapeEntity e in landscapeEntities)
                {
                    options.Add(new SelectListItem
                    {
                        Text = e.RowKey,
                        Value = e.RowKey
                    });
                }
            }
            catch
            {
                return null;
            }
            return Json(options);
        }

        [HttpGet]
        public async Task<LandscapeModel> GetById(string id, string partitionKey)
        {
            if (id == null || partitionKey == null) throw new ArgumentNullException();
            var landscapeEntity = await _landscapeService.GetByIdAsync(id, partitionKey);
            if (landscapeEntity == null || landscapeEntity.Landscape == null) throw new KeyNotFoundException();
            return JsonConvert.DeserializeObject<LandscapeModel>(landscapeEntity.Landscape);
        }

        // Format correctly for javascript consumption
        [HttpGet]
        public async Task<ActionResult> GetByIdJson(string id)
        {
            string environment = id.Substring(0, id.IndexOf('-'));
            LandscapeEntity landscape = await _landscapeService.GetByIdAsync(id, environment);
            if (landscape == null || landscape.Landscape == null) return NotFound();
            return Json(landscape.Landscape);
        }

        [HttpGet]
        public async Task<LandscapeModel> GetDefault()
        {
            LandscapeEntity defaultLandscape = await _landscapeService.GetDefault();
            if (defaultLandscape == null || defaultLandscape.Landscape == null) return null;
            return JsonConvert.DeserializeObject<LandscapeModel>(defaultLandscape.Landscape);
        }

        [HttpGet]
        public ActionResult GetDefaultJson()
        {
            LandscapeEntity landscapeEntity = _landscapeService.GetDefault().Result;
            if (landscapeEntity == null) return NotFound();
            return Json(landscapeEntity.Landscape);
        }

        [ActionName("Create")]
        public IActionResult Create()
        {
            return View(landscapeView);
        }

        [HttpPost]
        [ActionName("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAsync(LandscapeModel landscape)
        {
            if (ModelState.IsValid || landscape.IsDefault)
            {
                try
                {
                    if (landscape.IsDefault)
                    {
                        await UnsetDefault(landscape.Id);
                    }
                    landscape.Id = Helper.GenerateId(landscape);
                    await _landscapeService.CreateAsync(new LandscapeEntity(landscape));
                    TempData["success"] = "Successfully created workload zone " + landscape.Id;
                    return RedirectToAction("Index");
                }
                catch (Exception e)
                {
                    ModelState.AddModelError("LandscapeId", "Error creating workload zone: " + e.Message);
                }
            }

            landscapeView.Landscape = landscape;

            return View(landscapeView);
        }

        [ActionName("Deploy")]
        public async Task<IActionResult> DeployAsync(string id, string partitionKey)
        {
            try
            {
                LandscapeModel landscape = await GetById(id, partitionKey);

                List<SelectListItem> environments = restHelper.GetEnvironmentsList().Result;
                ViewBag.Environments = environments;

                return View(landscape);
            }
            catch (Exception e)
            {
                TempData["error"] = e.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ActionName("Deploy")]
        public async Task<RedirectToActionResult> DeployConfirmedAsync(string id, string partitionKey, string environment, string workload_environment)
        {
            try
            {
                LandscapeModel landscape = await GetById(id, partitionKey);

                string path = $"/LANDSCAPE/{id}/{id}.tfvars";
                string content = Helper.ConvertToTerraform(landscape);
                string pipelineId = _configuration["WORKLOADZONE_PIPELINE_ID"];
                bool isSystem = false;

                await restHelper.UpdateRepo(path, content);
                await restHelper.TriggerPipeline(pipelineId, id, isSystem, workload_environment, environment);
                
                TempData["success"] = "Successfully triggered workload zone deployment pipeline for " + id;
            }
            catch (Exception e)
            {
                TempData["error"] = "Error deploying workload zone " + id + ": " + e.Message;
            }
            return RedirectToAction("Index");
        }

        // [ActionName("Delete")]
        // public async Task<IActionResult> DeleteAsync(string id)
        // {
        //     if (id == null)
        //     {
        //         return BadRequest();
        //     }

        //     LandscapeModel landscape = await _landscapeService.GetByIdAsync(id);
        //     if (landscape == null)
        //     {
        //         return NotFound();
        //     }

        //     return View(landscape);
        // }

        // [HttpPost]
        // [ActionName("Delete")]
        // [ValidateAntiForgeryToken]
        // public async Task<IActionResult> DeleteConfirmedAsync(string id)
        // {
        //     await _landscapeService.DeleteAsync(id);
        //     TempData["success"] = "Successfully deleted workload zone " + id;
        //     return RedirectToAction("Index");
        // }

        [ActionName("Edit")]
        public async Task<IActionResult> EditAsync(string id, string partitionKey)
        {
            try
            {
                ActionResult<LandscapeModel> result = await GetById(id, partitionKey);
                LandscapeModel landscape = result.Value;
                landscapeView.Landscape = landscape;
                return View(landscapeView);
            }
            catch (Exception e)
            {
                TempData["error"] = e.Message;
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        [ActionName("Edit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAsync(LandscapeModel landscape)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    string newId = Helper.GenerateId(landscape);
                    if (landscape.Id == null) landscape.Id = newId;
                    if (newId != landscape.Id)
                    {
                        landscape.Id = newId;
                        return SubmitNewAsync(landscape).Result;
                    }
                    else
                    {
                        if (landscape.IsDefault)
                        {
                            await UnsetDefault(landscape.Id);
                        }
                        await _landscapeService.UpdateAsync(new LandscapeEntity(landscape));
                        TempData["success"] = "Successfully updated workload zone " + landscape.Id;
                        return RedirectToAction("Index");
                    }
                }
                catch (Exception e)
                {
                    ModelState.AddModelError("LandscapeId", "Error editing workload zone: " + e.Message);
                }
            }

            landscapeView.Landscape = landscape;

            return View(landscapeView);
        }

        [HttpPost]
        [ActionName("SubmitNew")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitNewAsync(LandscapeModel landscape)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    if (landscape.IsDefault)
                    {
                        await UnsetDefault(landscape.Id);
                    }
                    landscape.Id = Helper.GenerateId(landscape);
                    await _landscapeService.CreateAsync(new LandscapeEntity(landscape));
                    TempData["success"] = "Successfully created workload zone " + landscape.Id;
                    return RedirectToAction("Index");
                }
                catch (Exception e)
                {
                    ModelState.AddModelError("LandscapeId", "Error creating workload zone: " + e.Message);
                }
            }

            landscapeView.Landscape = landscape;

            return View("Edit", landscapeView);
        }

        [ActionName("Details")]
        public async Task<IActionResult> DetailsAsync(string id, string partitionKey)
        {
            try
            {
                ActionResult<LandscapeModel> result = await GetById(id, partitionKey);
                LandscapeModel landscape = result.Value;
                landscapeView.Landscape = landscape;
                return View(landscapeView);
            }
            catch (Exception e) 
            { 
                TempData["error"] = e.Message; 
                return RedirectToAction("Index"); 
            }
        }

        [ActionName("Download")]
        public ActionResult DownloadFile(string id, string partitionKey)
        {
            try
            {
                LandscapeModel landscape = GetById(id, partitionKey).Result;

                string path = $"{id}.tfvars";
                string content = Helper.ConvertToTerraform(landscape);

                var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
                return new FileStreamResult(stream, new MediaTypeHeaderValue("text/plain"))
                {
                    FileDownloadName = path
                };
            }
            catch (Exception e)
            {
                TempData["error"] = "Something went wrong downloading file " + id + ": " + e.Message;
                return RedirectToAction("Index");
            }
        }

        [ActionName("MakeDefault")]
        public async Task<IActionResult> MakeDefault(string id, string partitionKey)
        {
            try
            {
                await UnsetDefault(id);
                
                ActionResult<LandscapeModel> result = await GetById(id, partitionKey);
                LandscapeModel landscape = result.Value;

                landscape.IsDefault = true;
                LandscapeEntity landscapeEntity = new LandscapeEntity(landscape);
                await _landscapeService.UpdateAsync(landscapeEntity);
                TempData["success"] = id + " is now the default workload zone";
            }
            catch (Exception e)
            {
                TempData["error"] = "Error setting default for workload zone: " + e.Message;
            }
            return RedirectToAction("Index");
        }

        public async Task UnsetDefault(string id)
        {
            try
            {
                LandscapeModel existingDefault = await GetDefault();
                if (existingDefault != null && existingDefault.Id != id)
                {
                    existingDefault.IsDefault = false;
                    await _landscapeService.UpdateAsync(new LandscapeEntity(existingDefault));
                    Console.WriteLine("Unset existing default " + existingDefault.Id);
                }
            }
            catch (Exception e)
            {
                throw new Exception("Error unsetting the current default object: " + e.Message);
            }
        }
        
    }
}