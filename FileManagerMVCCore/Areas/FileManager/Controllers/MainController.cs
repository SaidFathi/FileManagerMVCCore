using System;
using System.Collections.Generic;
//using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
//using System.Web.Mvc;
using FileManagerMVCCore.Areas.FileManager.Entities;
using FileManagerMVCCore.Areas.FileManager.Models;
using FileManagerMVCCore.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace FileManagerMVCCore.Areas.FileManager.Controllers
{
    public class MainController : Controller
    {
        private readonly AppDataContext db ;
        private readonly IHostingEnvironment _hosting;
        private string RootPath = "/File-Repository/";
        private List<FileItem> Items;

        public MainController(AppDataContext db, IHostingEnvironment hosting)
        {
            this.db = db;
            _hosting = hosting;
        }

        // GET: FileManager/Main
        public ActionResult Index()
        {
            return View();
        }
        // GET: FileManager/Main/_Index
        public ActionResult _Index(string path)
        {
            return PartialView();
        }

        [HttpGet]
        public async Task<ActionResult> Update(string path)
        {
            try
            {
                path = path.Trim('/');
                // get current files & folders
                var items = db.FileItems.Where(x => x.Path.Equals(path)).OrderByDescending(x => x.IsFolder).Select(x => new FileItemModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Path = x.Path,
                    MimeType = x.MimeType,
                    CDate = x.CDate,
                    MDate = x.MDate,
                    IsFolder = x.IsFolder
                });

                return Json(new OperationResult
                {
                    Status = OperationStats.Success,
                    Items = await items.ToListAsync()
                });

            }
            catch (Exception ex)
            {
                throw;
            }
        }

        [HttpPost]
        public async Task<ActionResult> Create(CreateFileItemModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new OperationResult
                {
                    Status = OperationStats.Error,
                    Errors = GetErrors(ModelState)
                });
            }

            model.Path = model.Path.Trim('/');
           var absPath = _hosting.ContentRootPath + model.Path.Replace("ROOT", RootPath).Replace("/", "\\")+"\\" + model.Name;
                if (absPath.Contains("\\\\")) absPath=absPath.Replace("\\\\", "\\");
            var created = false;
            try
            {
                if (model.IsFolder)
                {
                    if (!Directory.Exists(absPath))
                    {
                        Directory.CreateDirectory(absPath);
                        created = true;
                    }
                }
                else
                {
                    if (!System.IO.File.Exists(absPath))
                    {
                        System.IO.File.WriteAllBytes(absPath, new byte[0]);
                        created = true;
                    }
                }

                if (created)
                {
                    // add to database
                    FileItem newEntity = new FileItem
                    {
                        Name = model.Name,
                        MimeType = model.Name.Contains('.') ? model.Name.Split('.').LastOrDefault() : null,
                        Path = model.Path,
                        IsFolder = model.IsFolder,
                        CDate = DateTime.UtcNow,
                        MDate = DateTime.UtcNow,
                    };

                    if (await db.FileItems.AnyAsync())
                    {
                        var p = string.Join("/", model.Path.Split('/').Reverse().Skip(1).Reverse().ToArray());
                        FileItem parent = await db.FileItems
                            .FirstOrDefaultAsync(x => x.Path.Equals(p));
                        if (parent != null)
                            newEntity.FileId = parent.Id;
                    }

                    db.FileItems.Add(newEntity);
                }
            }
            catch (Exception ex)
            {
                throw;
            }

            if (await db.SaveChangesAsync() > 0)
            {
                return Json(new OperationResult
                {
                    Status = OperationStats.Success,
                    Message = StringResources.SuccessfullyCreated
                });
            }

            return Json(new OperationResult
            {
                Status = OperationStats.Error,
                Message = StringResources.UnknownErrorOccurred
            });
        }

        [HttpPost]
        public async Task<ActionResult> Upload(UploadFileItemModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new OperationResult
                {
                    Status = OperationStats.Error,
                    Errors = GetErrors(ModelState)
                });
            }

            model.Path = model.Path.Trim('/');
            List<FileItem> listToAdd = new List<FileItem>();
            FileItem sibling = await db.FileItems.FirstOrDefaultAsync(x => x.Path.Equals(model.Path));

            try
            {
            var absPath = _hosting.ContentRootPath + model.Path.Replace("ROOT", RootPath).Replace("/", "\\") +"\\"+ model.PostedFile.FileName;
                if (absPath.Contains("\\\\")) absPath=absPath.Replace("\\\\", "\\");
                if (System.IO.File.Exists(absPath))
                {
                    return Json(new OperationResult
                    {
                        Status = OperationStats.Error,
                        Message = string.Format(StringResources.ItemAlreadyExists, model.PostedFile.FileName)
                    });
                }
                model.PostedFile.CopyTo(new FileStream(absPath, FileMode.Create));
                //model.PostedFile.SaveAs(absPath);
                listToAdd.Add(new FileItem
                {
                    Name = model.PostedFile.FileName,
                    MimeType = model.PostedFile.ContentType,
                    Path = model.Path.Trim('/'),
                    CDate = DateTime.UtcNow,
                    MDate = DateTime.UtcNow,
                    FileId = sibling?.FileId
                });

                if (!listToAdd.Any())
                    return Json(new OperationResult
                    {
                        Status = OperationStats.Error,
                        Message = StringResources.UnknownErrorOccurred
                    });

                db.FileItems.AddRange(listToAdd);
                await db.SaveChangesAsync();

                return Json(new OperationResult
                {
                    Status = OperationStats.Success,
                    Message = string.Format(StringResources.SuccessfullyUploaded, model.PostedFile.FileName)
                });
            }
            catch (Exception ex)
            {
                return Json(new OperationResult
                {
                    Status = OperationStats.Success,
                    Message = StringResources.CodeError
                });
            }
        }

        [HttpGet]
        public async Task<ActionResult> Edit(int id)
        {
            try
            {
                FileItem file = await db.FileItems.FindAsync(id);

                if (file == null)
                {
                    return RedirectToAction("Index");
                }
            string path = _hosting.ContentRootPath + file.Path.Replace("ROOT", RootPath).Replace("/", "\\") +"\\"+ file.Name;
                if (path.Contains("\\\\")) path = path.Replace("\\\\", "\\");

                if (!System.IO.File.Exists(path))
                {
                    return RedirectToAction("Index");
                }

                var result = new EditFileItemModel
                {
                    Id = file.Id,
                    Path = file.Path,
                    Name = file.Name,
                    CDate = file.CDate,
                    MDate = file.MDate
                };

                using (StreamReader sr = new StreamReader(path))
                {
                    result.Content = await sr.ReadToEndAsync();
                }

                return View(result);
            }
            catch (Exception ex)
            {
                return Json(new OperationResult
                {
                    Status = OperationStats.Success,
                    Message = StringResources.CodeError
                });
            }
        }

        [HttpGet]
        public async Task<ActionResult> _Edit(int id)
        {
            try
            {
                FileItem file = await db.FileItems.FindAsync(id);

                if (file == null)
                {
                    return RedirectToAction("Index");
                }
            string path = _hosting.ContentRootPath + file.Path.Replace("ROOT", RootPath).Replace("/", "\\") +"\\"+ file.Name;
                if (path.Contains("\\\\")) path=path.Replace("\\\\", "\\");
                if (!System.IO.File.Exists(path))
                {
                    return RedirectToAction("Index");
                }

                var result = new EditFileItemModel
                {
                    Id = file.Id,
                    Path = file.Path,
                    Name = file.Name,
                    CDate = file.CDate,
                    MDate = file.MDate
                };

                using (StreamReader sr = new StreamReader(path))
                {
                    result.Content = await sr.ReadToEndAsync();
                }

                return View(result);
            }
            catch (Exception ex)
            {
                return Json(new OperationResult
                {
                    Status = OperationStats.Success,
                    Message = StringResources.CodeError
                });
            }
        }

        [HttpPost]
        public async Task<ActionResult> Rename(EditFileItemModel model)
        {
            try
            {
                FileItem file = await db.FileItems.FindAsync(model.Id);

                if (file == null)
                {
                    return Json(new OperationResult
                    {
                        Status = OperationStats.Error,
                        Message = StringResources.NotFoundInDatabase,
                    });
                }

               string absPath =_hosting.ContentRootPath+file.Path.Replace("ROOT", RootPath).Replace("/","\\")+"\\"+ file.Name;
                if (absPath.Contains("\\\\")) absPath=absPath.Replace("\\\\", "\\");
                if (model.IsFolder)
                {
                    if (!Directory.Exists(absPath))
                    {
                        return Json(new OperationResult
                        {
                            Status = OperationStats.Error,
                            Message = StringResources.NotFoundInFileSystem,
                        });
                    }
                    // Rename the name of current directory
                    // Rename in File System
                    Directory.Move(absPath, RenameFileOrDirectory(absPath, model.Name));
                    // Rename in Database
                    file.Name = model.Name;
                    file.MDate = DateTime.UtcNow;
                    await db.SaveChangesAsync();

                    // Change sub directory and file pathes
                    await UpdateSubDirectoryPath(await db.FileItems.Where(x => x.FileId != null && x.FileId == file.Id).ToListAsync());
                }
                else
                {
                    if (!System.IO.File.Exists(absPath))
                    {
                        return Json(new OperationResult
                        {
                            Status = OperationStats.Error,
                            Message = StringResources.NotFoundInFileSystem,
                        });
                    }
                    // Rename in File System
                    System.IO.File.Move(absPath, RenameFileOrDirectory(absPath, model.Name));

                    // Rename in Database
                    file.Name = model.Name;
                    file.MDate = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }

                await db.SaveChangesAsync();

                return Json(new OperationResult
                {
                    Status = OperationStats.Success,
                    Message = StringResources.NameChanged,
                });
            }
            catch (Exception ex)
            {
                return Json(new OperationResult
                {
                    Status = OperationStats.Success,
                    Message = StringResources.CodeError
                });
            }
        }


        [HttpPost]
        public async Task<ActionResult> Delete(int id)
        {
            try
            {
                FileItem file = await db.FileItems.FindAsync(id);

                if (file == null)
                {
                    return Json(new OperationResult
                    {
                        Status = OperationStats.Error,
                        Message = StringResources.NotFoundInDatabase,
                    });
                }

                string path = _hosting.ContentRootPath + file.Path.Replace("ROOT", RootPath).Replace("/", "\\") +"\\"+ file.Name;
                if (path.Contains("\\\\")) path = path.Replace("\\\\", "\\");
                if (file.IsFolder)
                {
                    if (!Directory.Exists(path))
                    {
                        return Json(new OperationResult
                        {
                            Status = OperationStats.Error,
                            Message = StringResources.NotFoundInFileSystem,
                        });
                    }
                    // Remove it from File System
                    Directory.Delete(path, true);
                }
                else
                {
                    if (!System.IO.File.Exists(path))
                    {
                        return Json(new OperationResult
                        {
                            Status = OperationStats.Error,
                            Message = StringResources.NotFoundInFileSystem,
                        });
                    }
                    // Remove it from File System
                    System.IO.File.Delete(path);
                }

                // Remove it's sub items and itself from Database
                Items = new List<FileItem>();
                await GetSubItemsAsync(await db.FileItems.Where(x => x.Id == file.Id).ToListAsync());
                Items.Reverse();
                foreach (var item in Items)
                {
                    db.FileItems.Remove(item);
                }

                db.FileItems.Remove(file);
                await db.SaveChangesAsync();

                return Json(new OperationResult
                {
                    Status = OperationStats.Success,
                    Message = StringResources.SuccessfullyDeleted
                });
            }
            catch (Exception ex)
            {
                return Json(new OperationResult
                {
                    Status = OperationStats.Success,
                    Message = StringResources.CodeError
                });
            }
        }
        private async Task GetSubItemsAsync(List<FileItem> items)
        {
            
            foreach (FileItem item in items)
            {
                Items.Add(item);
                await GetSubItemsAsync(item.Files.ToList());
                
            }
        }

        private async Task UpdateSubDirectoryPath(List<FileItem> items)
        {
            foreach (var item in items)
            {
                item.Path = string.Concat(item.File.Path, '/', item.File.Name);
                await db.SaveChangesAsync();
                await UpdateSubDirectoryPath(item.Files.ToList());
            }
        }

        private string RenameFileOrDirectory(string path, string newName)
        {
            var dirName = string.Join("\\", path.Split('\\').Reverse().Skip(1).Reverse());

            return string.Concat(dirName, '\\', newName);
        }

        private List<ModelErrorCollection> GetErrors(ModelStateDictionary modelState)
        {
            return modelState.Select(x => x.Value.Errors)
                .Where(y => y.Count > 0)
                .ToList();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();

            base.Dispose(disposing);
        }
    }
}