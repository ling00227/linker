<template>
    <div class="logger-setting-wrap flex flex-column h-100" ref="wrap">
        <el-tabs type="border-card" class="w-100">
            <el-tab-pane :label="$t('logger.list')" v-if="hasLogger">
                <div class="inner">
                    <div class="head flex">
                        <div>
                            <el-select v-model="state.type" @change="loadData" size="small" class="mgr-1" style="width: 6rem;">
                                <el-option :value="-1" label="all"></el-option>
                                <el-option :value="0" label="debug"></el-option>
                                <el-option :value="1" label="info"></el-option>
                                <el-option :value="2" label="warning"></el-option>
                                <el-option :value="3" label="error"></el-option>
                                <el-option :value="4" label="fatal"></el-option>
                            </el-select>
                        </div>
                        <el-button type="warning" size="small" :loading="state.loading" @click="clearData">{{$t('logger.clear')}}</el-button>
                        <el-button size="small" :loading="state.loading" @click="loadData">{{$t('logger.refresh')}}</el-button>
                        <span class="flex-1"></span>
                    </div>
                    <div class="body flex-1 relative">
                        <el-table stripe border :data="state.page.List" size="small" :height="`${state.height}px`" width="100%" @row-click="handleRowClick" :row-class-name="tableRowClassName">
                            <el-table-column type="index" width="50" />
                            <el-table-column prop="Type" :label="$t('logger.level')" width="80">
                                <template #default="scope">
                                    <span>{{state.types[scope.row.Type]}} </span>
                                </template>
                            </el-table-column>
                            <el-table-column prop="Time" :label="$t('logger.time')" width="160"></el-table-column>
                            <el-table-column prop="content" :label="$t('logger.content')"></el-table-column>
                        </el-table>
                    </div>
                    <div class="pages t-c">
                        <div class="page-wrap">
                            <el-pagination small :total="state.page.Count"
                             v-model:currentPage="state.page.Page" :page-size="state.page.Size" 
                             :pager-count="globalData.isPc?7:3"
                             :layout="globalData.isPc?'total,prev, pager, next':'prev, pager, next'"
                             @current-change="handlePageChange" background 
                           >
                            </el-pagination>
                        </div>
                    </div>
                </div>
            </el-tab-pane>
            <el-tab-pane :label="$t('common.setting')" v-if="hasLoggerLevel">
                <Setting></Setting>
            </el-tab-pane>
        </el-tabs>
    </div>

</template>

<script>
import { reactive,computed } from '@vue/reactivity'
import { getLogger, clearLogger } from '@/apis/logger'
import { onMounted } from '@vue/runtime-core'
import Setting from './Setting.vue'
import { ElMessageBox } from 'element-plus'
import {  ref } from 'vue'
import { injectGlobalData } from '@/provide'
export default {
    components: { Setting },
    setup() {
        const globalData = injectGlobalData();
        const hasLogger = computed(()=>globalData.value.hasAccess('LoggerShow'));
        const hasLoggerLevel = computed(()=>globalData.value.hasAccess('LoggerLevel'));
        const wrap = ref(null);
        const state = reactive({
            loading: true,
            type:-1,
            page: { Page: 1, Size: 20, Count: 0, List: [] },
            types: ['debug', 'info', 'warning', 'error', 'fatal'],
            height:computed(()=>globalData.value.height - 180),
        })
        const loadData = () => {
            state.loading = true;
            getLogger({
                Page : state.page.Page,
                Size : state.page.Size,
                Type : state.type
            }).then((res) => {
                state.loading = false;
                res.List.map(c => {
                    c.content = c.Content.substring(0, 50);
                });
                state.page = res;
            }).catch((err) => {
                console.log(err);
                state.loading = false;
            });
        }
        const handlePageChange = (page)=>{
            if(page){
                state.page.Page = page;
                loadData();
            }
        }
        const clearData = () => {
            state.loading = true;
            clearLogger().then(() => {
                state.loading = false;
                loadData();
            }).catch(() => {
                state.loading = false;
            });
        }

        const tableRowClassName = ({ row, rowIndex }) => {
            return `type-${row.Type}`;
        }
        const handleRowClick = (row, column, event) => {
            let css = `padding:1rem;border:1px solid #ddd; resize:none;width:30rem;box-sizing: border-box;white-space: nowrap; height:30rem;`;
            
            ElMessageBox.alert(`<textarea class="scrollbar-4" style="${css}">${row.Content}</textarea>`, '', {
                dangerouslyUseHTMLString: true,
            }).catch(()=>{});
        }

        onMounted(()=>{
            loadData();
        });

        return {
            globalData,hasLogger,hasLoggerLevel,wrap,state, loadData, clearData, tableRowClassName, handleRowClick,handlePageChange
        }
    }
}
</script>
<style lang="stylus" scoped>
.pages {
    padding: 1rem 0 0 1rem;
}

.page-wrap{
        display:inline-block;
    }

.logger-setting-wrap {
    padding: 1rem;
    box-sizing: border-box;

    .inner {
        padding: 1rem;
    }

    .head {
        margin-bottom: 1rem;
    }
}
</style>
<style  lang="stylus">
.logger-setting-wrap {
    .el-table {
        .type-0 {
            color: blue;
        }

        .type-1 {
            color: #333;
        }

        .type-2 {
            color: #cd9906;
        }

        .type-3 {
            color: red;
        }

        .type-4 {
            color: red;
            font-weight: bold;
        }
    }
}
</style>