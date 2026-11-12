<template>
  <div class="q-pa-md wechat-settings">
    <div class="row items-center q-mb-md q-gutter-sm">
      <div class="text-h5 col">公众号配置</div>
      <q-btn outline label="重新加载" icon="refresh" :disable="saving || testing" :loading="loading" @click="load" />
      <q-btn outline label="测试连接" icon="wifi" color="info" :disable="!loaded || saving" :loading="testing" @click="testConnection" />
      <q-btn color="primary" label="保存配置" icon="save" :disable="!loaded || loading" :loading="saving" @click="save" />
    </div>
    <q-banner v-if="message" class="q-mb-md" :class="failed ? 'bg-red-1 text-negative' : 'bg-green-1 text-positive'" role="status">{{ message }}</q-banner>
    <q-card flat bordered>
      <q-card-section class="q-gutter-md">
        <div class="text-h6">公众号凭据</div>
        <p class="text-grey">在公众号后台「设置与开发 → 基本配置」中获取 AppID 与 AppSecret。AppSecret 只保存在服务器、不会回显；调用微信接口的服务器出口 IP 必须先加入公众号后台的 IP 白名单，否则会报 40164 错误。草稿箱与发布接口需要已认证的公众号，未认证的订阅号会报 48001 无权限。</p>
        <q-toggle v-model="settings.Enabled" label="启用公众号同步" :disable="!loaded || saving" />
        <div><q-chip :color="settings.HasSecret ? 'positive' : 'grey'" text-color="white">{{ settings.HasSecret ? 'AppSecret 已配置' : 'AppSecret 未配置' }}</q-chip></div>
        <div class="row q-col-gutter-md">
          <div class="col-12 col-md-6">
            <q-input v-model="settings.AppId" label="AppID" outlined autocomplete="off" hint="公众号的 AppID（开发者 ID）" :disable="!loaded || saving" />
          </div>
          <div class="col-12 col-md-6">
            <q-input v-model="settings.AppSecret" type="password" label="AppSecret" outlined autocomplete="new-password" hint="输入新的 AppSecret；留空保留已保存的值" :disable="!loaded || saving || settings.ClearSecret" />
          </div>
        </div>
        <q-checkbox v-model="settings.ClearSecret" label="清除已保存的 AppSecret" :disable="!loaded || saving" @update:model-value="settings.AppSecret = ''" />
      </q-card-section>
      <q-separator />
      <q-card-section>
        <div class="text-h6 q-mb-md">同步设置</div>
        <div class="row q-col-gutter-md">
          <div class="col-12 col-md-6">
            <q-input v-model="settings.Author" label="默认作者" outlined maxlength="8" counter hint="留空使用文章作者；微信侧作者最长 8 个字" :disable="!loaded || saving" />
          </div>
          <div class="col-12 col-md-6">
            <q-input v-model="settings.DefaultCoverUrl" label="默认封面图地址" outlined hint="文章正文没有图片时用作公众号封面；可填本站相对路径（如 /upload/images/x.jpg）或图片外链。不填则依次回退使用站点 Logo、系统自动生成的兜底封面，纯文字文章也能同步" :disable="!loaded || saving" />
          </div>
        </div>
        <div class="q-mt-sm">
          <q-toggle v-model="settings.AutoSync" label="文章发布/编辑保存后自动同步到草稿箱" :disable="!loaded || saving" />
          <div class="text-caption text-grey q-ml-sm">开启后，每次在后台发布或修改文章，后台任务会自动把最新版同步到公众号草稿箱（已同步过的更新原草稿）。</div>
        </div>
        <div>
          <q-toggle v-model="settings.AutoPublish" label="同步后直接发布" :disable="!loaded || saving" />
          <div class="text-caption text-grey q-ml-sm">默认关：文章只进草稿箱，去公众号后台确认排版后再发表。打开后手动同步与自动同步都会直接提交发布（单次同步时仍可在弹窗里改选）。</div>
        </div>
        <div>
          <q-toggle v-model="settings.OpenComment" label="开启评论" :disable="!loaded || saving" />
          <q-toggle v-model="settings.FansOnlyComment" label="仅粉丝可评论" :disable="!loaded || saving || !settings.OpenComment" />
        </div>
      </q-card-section>
      <q-separator />
      <q-card-section>
        <div class="text-h6 q-mb-sm">同步说明</div>
        <div class="text-body2">
          <p>· 同步入口：文章列表页每行的「公众号同步」按钮，或文章编辑页的「同步到公众号」按钮；已同步过的文章再次同步会更新原草稿，不会重复创建。</p>
          <p>· 正文处理：Markdown 文章会先转 HTML；正文图片会逐张转存到微信图床（微信会过滤外部图片地址）；封面取正文第一张图，没有图时用上面的默认封面。</p>
          <p>· 微信限制：标题最长 64 字、作者最长 8 字、摘要最长 120 字（自动截取正文开头）、正文少于 2 万字符，超限的文章同步时会给出提示。</p>
        </div>
      </q-card-section>
    </q-card>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref, onMounted } from "vue";
import api from "@/axios/AxiosConfig";

interface Settings {
  Enabled: boolean;
  AppId: string;
  HasSecret: boolean;
  AppSecret: string;
  ClearSecret: boolean;
  Author: string;
  DefaultCoverUrl: string;
  AutoPublish: boolean;
  OpenComment: boolean;
  FansOnlyComment: boolean;
  AutoSync: boolean;
}
interface Response { Success: boolean; Message: string; Data: any }

const settings = reactive<Settings>({
  Enabled: false,
  AppId: "",
  HasSecret: false,
  AppSecret: "",
  ClearSecret: false,
  Author: "",
  DefaultCoverUrl: "",
  AutoPublish: false,
  OpenComment: false,
  FansOnlyComment: false,
  AutoSync: false
});
const loading = ref(false); const saving = ref(false); const testing = ref(false);
const loaded = ref(false); const message = ref(""); const failed = ref(false);

async function load() {
  loading.value = true;
  loaded.value = false;
  try {
    const response = await api.get("/wechat/getsettings") as unknown as Response;
    if (!response.Success || !response.Data) throw new Error(response.Message || "加载公众号配置失败");
    Object.assign(settings, response.Data, { AppSecret: "", ClearSecret: false });
    loaded.value = true;
  } catch { failed.value = true; message.value = "加载公众号配置失败，请检查登录状态后重试。"; } finally { loading.value = false; }
}

async function save() {
  if (!loaded.value) return;
  saving.value = true;
  message.value = "";
  try {
    const { HasSecret, ...command } = settings;
    const response = await api.post("/wechat/savesettings", command) as unknown as Response;
    if (!response.Success) throw new Error(response.Message || "保存失败");
    settings.HasSecret = command.ClearSecret ? false : !!command.AppSecret.trim() || HasSecret;
    settings.AppSecret = "";
    settings.ClearSecret = false;
    failed.value = false;
    message.value = response.Message || "公众号配置已保存。";
  } catch (error) { failed.value = true; message.value = error instanceof Error ? error.message : "保存失败，请重试。"; } finally { saving.value = false; }
}

async function testConnection() {
  testing.value = true;
  message.value = "";
  try {
    const response = await api.post("/wechat/testconnection", {}) as unknown as Response;
    failed.value = !response.Success;
    message.value = response.Message || (response.Success ? "连接成功！" : "连接失败");
  } catch { failed.value = true; message.value = "测试连接失败，请检查登录状态后重试。"; } finally { testing.value = false; }
}

onMounted(load);
</script>

<style scoped>
.wechat-settings { max-width: 1100px; margin: auto; }
</style>
