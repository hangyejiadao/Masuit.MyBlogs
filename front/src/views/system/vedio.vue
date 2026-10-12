<template>
  <div class="q-pa-md vedio-settings">
    <div class="row items-center q-mb-md q-gutter-sm">
      <div class="text-h5 col">影视设置</div>
      <q-btn outline label="重新加载" icon="refresh" :disable="saving" :loading="loading" @click="load" />
      <q-btn color="primary" label="保存配置" icon="save" :disable="!loaded || loading" :loading="saving" @click="save" />
    </div>
    <q-banner v-if="message" class="q-mb-md" :class="failed ? 'bg-red-1 text-negative' : 'bg-green-1 text-positive'" role="status">{{ message }}</q-banner>
    <q-card flat bordered>
      <q-card-section class="q-gutter-md">
        <div class="text-h6">服务配置</div>
        <p>配置保存在服务器，访问 /vedio 的用户通过接口获取配置。TMDB Token 只用于服务器请求，不会返回给访客。</p>
        <q-toggle v-model="settings.Enabled" label="启用影视服务" :disable="!loaded || saving" />
        <div><q-chip :color="settings.HasToken ? 'positive' : 'grey'" text-color="white">{{ settings.HasToken ? 'Token 已配置' : 'Token 未配置' }}</q-chip></div>
        <q-input v-model="settings.TmdbToken" type="password" autocomplete="new-password" label="TMDB Read Access Token" outlined
          hint="输入 TMDB 的 API Read Access Token；留空保留原 Token。" :disable="!loaded || saving || settings.ClearToken" />
        <q-checkbox v-model="settings.ClearToken" label="清除已保存的 Token" :disable="!loaded || saving" @update:model-value="settings.TmdbToken = ''" />
      </q-card-section>
      <q-separator />
      <q-card-section>
        <div class="text-h6 q-mb-md">影视与外观</div>
        <div class="row q-col-gutter-md">
          <div class="col-12 col-md-6"><q-select v-model="settings.Language" :options="languages" label="影视资料语言" outlined emit-value map-options :disable="!loaded || saving" /></div>
          <div class="col-12 col-md-6"><q-select v-model="settings.PlayerSource" :options="sources" label="默认播放源" outlined emit-value map-options :disable="!loaded || saving" /></div>
          <div class="col-12 col-md-6"><q-select v-model="settings.Theme" :options="themes" label="主题" outlined emit-value map-options :disable="!loaded || saving" /></div>
          <div class="col-12 col-md-6"><q-select v-model="settings.AccentColor" :options="accents" label="强调色" outlined emit-value map-options :disable="!loaded || saving" /></div>
        </div>
        <q-toggle class="q-mt-md" v-model="settings.HistoryEnabled" label="保存观看历史" :disable="!loaded || saving" />
        <p class="text-grey q-mt-sm">收藏和观看记录仍保存在各自浏览器。保存后刷新影视页面即可应用新配置。</p>
      </q-card-section>
    </q-card>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref, onMounted } from "vue";
import api from "@/axios/AxiosConfig";

interface Settings { Enabled: boolean; HasToken: boolean; TmdbToken: string; ClearToken: boolean; Language: string; PlayerSource: string; Theme: string; AccentColor: string; HistoryEnabled: boolean }
interface Response { Success: boolean; Message: string; Data: Omit<Settings, "TmdbToken" | "ClearToken"> }
const settings = reactive<Settings>({ Enabled: true, HasToken: false, TmdbToken: "", ClearToken: false, Language: "en-US", PlayerSource: "videasy", Theme: "dark", AccentColor: "red", HistoryEnabled: true });
const loading = ref(false); const saving = ref(false); const loaded = ref(false); const message = ref(""); const failed = ref(false);
const languages = [{ label: "简体中文", value: "zh-CN" }, { label: "繁體中文", value: "zh-TW" }, { label: "English", value: "en-US" }, { label: "日本語", value: "ja-JP" }, { label: "Deutsch", value: "de-DE" }];
const sources = [{ label: "Videasy", value: "videasy" }, { label: "VidSrc", value: "vidsrc" }, { label: "Vidking", value: "vidking" }];
const themes = [{ label: "深色", value: "dark" }, { label: "AMOLED", value: "amoled" }, { label: "Mocha", value: "mocha" }, { label: "Slate", value: "slate" }, { label: "浅色", value: "light" }];
const accents = [{ label: "红色", value: "red" }, { label: "蓝色", value: "blue" }, { label: "紫色", value: "purple" }, { label: "绿色", value: "green" }, { label: "橙色", value: "orange" }, { label: "粉色", value: "pink" }];
async function load() {
  loading.value = true;
  loaded.value = false;
  try {
    const response = await api.get("/system/getvediosettings") as unknown as Response;
    if (!response.Success || !response.Data) throw new Error(response.Message || "加载影视配置失败");
    Object.assign(settings, response.Data, { TmdbToken: "", ClearToken: false });
    loaded.value = true;
  } catch { failed.value = true; message.value = "加载影视配置失败，请检查登录状态后重试。"; } finally { loading.value = false; }
}
async function save() {
  if (!loaded.value) return;
  saving.value = true;
  message.value = "";
  try {
    const { HasToken, ...command } = settings;
    const response = await api.post("/system/savevediosettings", command) as unknown as Response;
    if (!response.Success) throw new Error(response.Message || "保存失败");
    settings.HasToken = command.ClearToken ? false : !!command.TmdbToken.trim() || HasToken;
    settings.TmdbToken = "";
    settings.ClearToken = false;
    failed.value = false;
    message.value = "影视设置已保存，刷新影视页面即可生效。";
  } catch (error) { failed.value = true; message.value = error instanceof Error ? error.message : "保存失败，请重试。"; } finally { saving.value = false; }
}
onMounted(load);
</script>

<style scoped>
.vedio-settings { max-width: 1100px; margin: auto; }
</style>
