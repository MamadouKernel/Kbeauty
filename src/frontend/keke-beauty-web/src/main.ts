import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { createAppConfig } from './app/app.config';
import { loadRuntimeConfig } from './app/core/config/runtime-config';

// Runtime settings (e.g. the Yango link template) must be known before providers are created.
loadRuntimeConfig()
  .then((runtime) => bootstrapApplication(App, createAppConfig(runtime)))
  .catch((err) => console.error(err));
