using System;
using System.IO;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.Extensions.Options;
using NetModular.Lib.Utils.Core.Extensions;
using NetModular.Lib.Utils.Core.Options;
using NetModular.Lib.Utils.Core.Result;
using NetModular.Module.Common.Application.AttachmentService.ResultModels;
using NetModular.Module.Common.Application.AttachmentService.ViewModels;
using NetModular.Module.Common.Domain.Attachment;
using NetModular.Module.Common.Domain.Attachment.Models;
using NetModular.Module.Common.Domain.AttachmentOwner;
using NetModular.Module.Common.Domain.MediaType;
using NetModular.Module.Common.Infrastructure.Repositories;
using FileInfo = NetModular.Lib.Utils.Core.Files.FileInfo;

namespace NetModular.Module.Common.Application.AttachmentService
{
    public class AttachmentService : IAttachmentService
    {
        private readonly ModuleCommonOptions _moduleCommonOptions;
        private readonly IMapper _mapper;
        private readonly IAttachmentRepository _repository;
        private readonly IAttachmentOwnerRepository _ownerRepository;
        private readonly IMediaTypeRepository _mediaTypeRepository;
        private readonly CommonDbContext _dbContext;

        public AttachmentService(IAttachmentRepository repository, IAttachmentOwnerRepository ownerRepository, IOptionsMonitor<ModuleCommonOptions> moduleCommonOptionsMonitor, IMediaTypeRepository mediaTypeRepository, IMapper mapper, CommonDbContext dbContext)
        {
            _repository = repository;
            _ownerRepository = ownerRepository;
            _moduleCommonOptions = moduleCommonOptionsMonitor.CurrentValue;
            _mediaTypeRepository = mediaTypeRepository;
            _mapper = mapper;
            _dbContext = dbContext;
        }

        public async Task<IResultModel> Query(AttachmentQueryModel model)
        {
            var result = new QueryResultModel<AttachmentEntity>
            {
                Rows = await _repository.Query(model),
                Total = model.TotalCount
            };
            return ResultModel.Success(result);
        }

        /// <summary>
        /// 删除
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<IResultModel> Delete(Guid id)
        {
            var entity = await _repository.GetAsync(id);
            if (entity == null)
                return ResultModel.NotExists;

            if (await _repository.DeleteAsync(id))
            {

                return ResultModel.Success();
            }

            return ResultModel.Failed();
        }

        public async Task<IResultModel<AttachmentUploadResultModel>> Upload(AttachmentUploadModel model, FileInfo fileInfo)
        {
            var result = new ResultModel<AttachmentUploadResultModel>();
            var entity = new AttachmentEntity
            {
                Module = model.Module,
                Group = model.Group,
                FileName = model.Name.NotNull() ? model.Name : fileInfo.FileName,
                SaveName = fileInfo.SaveName,
                Ext = fileInfo.Ext,
                Md5 = fileInfo.Md5,
                Path = fileInfo.Path,
                FullPath = Path.Combine(fileInfo.Path, fileInfo.SaveName),
                Size = fileInfo.Size.Size,
                SizeCn = fileInfo.Size.ToString()
            };

            var mediaType = await _mediaTypeRepository.GetByExt(fileInfo.Ext);
            if (mediaType != null)
            {
                entity.MediaType = mediaType.Value;
            }

            using (var uow = _dbContext.NewUnitOfWork())
            {
                if (await _repository.AddAsync(entity))
                {
                    var ownerEntity = new AttachmentOwnerEntity
                    {
                        AttachmentId = entity.Id,
                        AccountId = model.AccountId
                    };
                    if (!model.Auth || await _ownerRepository.AddAsync(ownerEntity, uow))
                    {
                        uow.Commit();

                        var resultModel = _mapper.Map<AttachmentUploadResultModel>(entity);

                        return result.Success(resultModel);
                    }
                }
            }

            return result.Failed("上传失败");
        }

        public async Task<IResultModel<FileDownloadModel>> Download(Guid id, Guid accountId)
        {
            var result = new ResultModel<FileDownloadModel>();

            var attachment = await _repository.GetAsync(id);
            if (attachment == null)
                return result.Failed("附件不存在");

            if (attachment.Auth)
            {
                var has = await _ownerRepository.Exist(new AttachmentOwnerEntity { AccountId = accountId, AttachmentId = id });
                if (!has)
                {
                    return result.Failed("无权访问");
                }
            }

            var filePath = Path.Combine(_moduleCommonOptions.UploadPath, attachment.FullPath);
            if (!File.Exists(filePath))
                return result.Failed("附件不存在");

            return result.Success(new FileDownloadModel(filePath, attachment.FileName, attachment.MediaType));
        }
    }
}
