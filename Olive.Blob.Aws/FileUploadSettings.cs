﻿using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Olive.BlobAws
{
    class FileUploadSettings
    {
        readonly IConfiguration Configuration;

        public FileUploadSettings(IConfiguration configuration) => Configuration = configuration;

        public string BucketName => Configuration.GetValue<string>("Aws:TempBucket");
        public string Region => Configuration.GetValue<string>("Aws:Region");
    }
}
